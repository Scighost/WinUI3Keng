# 自动构建与自动更新

> 本文记录的方法不是 WinUI 3 专属的，也可以用于其他框架。

某不能说名字的软件在开发时需要频繁更新版本，为了兼顾开发体验和用户体验，使用自动化流程达到这一目的。

## 自动构建

代码托管在 GitHub 上，使用 GitHub Actions 是最合适的选择。在写自动构建的流程之前，考虑了这么几个问题：

1. GitHub 在部分地区速度太慢或不可用，不适合作为安装包的下载来源，选择了白嫖阿里云 OSS 和 Cloudflare 代理。
2. 为了区分自动构建的版本和发行版，需要在版本号后加上 `-dev.*`，选择了在文件的 `ProductVersion` 属性中记录当前版本号。

### 准备环境

运行 Actions 的系统为 `windows-latest`，默认 Shell 为 `PowerShell`。

``` yml
- name: Checkout
  uses: actions/checkout@v3
  with:
    # 迁出所有代码，后面有用
    fetch-depth: 0

- name: Install .NET Core
  uses: actions/setup-dotnet@v3
  with:
    dotnet-version: 7.0.x

- name: Restore the Packages
  run: dotnet restore

# 配置 阿里云 OssUtil
- name: Setup Aliyun OssUtil
  run: |
    # 见下方 PowerShell 代码块
```

``` PowerShell
# 下载适合 Windows 的 OssUtil 安装包到临时文件夹
Invoke-WebRequest https://gosspublic.alicdn.com/ossutil/1.7.14/ossutil64.zip -OutFile ${{runner.temp}}/ossutil.zip
# 解压
Expand-Archive -Path ${{runner.temp}}/ossutil.zip -DestinationPath ${{runner.temp}}
# 把 exe 文件移动到 system32 文件夹下，为了在后续步骤中可以直接使用 ossutil 命令
Move-Item -Path ${{runner.temp}}/ossutil64/ossutil64.exe -Destination C:/Windows/System32/ossutil.exe -Force
# 配置账号
ossutil config -e ${{ secrets.OSS_ENDPOINT }} -i ${{ secrets.ACCESS_KEY_ID }} -k ${{ secrets.ACCESS_KEY_SECRET }}
```

### 版本号

每次构建的版本必须比上一次构建的版本要大，可以通过当前时间生成版本 `v1.2.3-dev.221015.2201`，或者通过环境变量 `${{github.run_number}}` 获取当前 Action 是第几次执行，就是 Actions 页面每次执行时 # 后面的那个数字。但是这两个方法生成的版本号是一直递增的，我比较倾向于使用 **上个版本发布后的提交数** 作为 `-dev.` 后的数字。

> 下面是生成目标版本号的函数，可以不用看

``` PowerShell
function Get-TargetVersion {
    # 获取上一个 tag，迁出代码时设置 fetch-depth: 0
    $lastTag = git describe --abbrev=0 --tags
    if ([String]::IsNullOrWhiteSpace($lastTag)) {
        # 没有 tag
        $lastTag = 'v0.1.0'
        # 提交次数
        $commitCount = git rev-list HEAD --count
    }else {
        # 有 tag
        $commitCount = git rev-list "$lastTag..HEAD" --count
    }
    # 把 tag 开头的 v 去掉
    if ($lastTag.StartsWith('v') -or $lastTag.StartsWith('V')) {
        $lastTag = $lastTag.SubString(1)
    }
    # 引入一个新库 NuGet.Versioning
    $null = [System.Reflection.Assembly]::LoadFrom('NuGet.Versioning.dll')
    # TryParse 方法中使用 out 修饰的参数必须先定义
    [ref]$lastVer = [NuGet.Versioning.SemanticVersion]::Parse('0.1.0')
    # 解析上一个版本号，有可能是 v1.2.3、v1.2.3-preview.1 或更复杂的内容
    if ([NuGet.Versioning.SemanticVersion]::TryParse($lastTag, $lastVer)) {
        if ($lastVer.Value.IsPrerelease) {
            # 上个版本是预发行版，直接在后面加上 -dev.*
            $targetVer = "$lastVer-dev.$commitCount"
        }
        else {
            # 上个版本是正式版，版本号+1后再接 -dev.*
            $targetVer = "$($lastVer.Value.Major).$($lastVer.Value.Minor).$($lastVer.Value.Patch+1)-dev.$commitCount"
        }
    }
    else {
        # 理论上来说不会运行到这里
        $targetVer = "$lastVer-dev.$commitCount"
    }
    Write-Output $targetVer
}
```

在这里使用了一个库 NuGet.Versioning，这是解析 **语义化版本 v2.0** 一个库，和 `System.Version` 仅支持 4 个数字不同，它支持解析和比较如 `1.2.3-preview.4.5.6+abcdef` 这样的格式。

### 发布

``` PowerShell
$v = Get-TargetVersion
dotnet publish -p:Configuration=$env:Configuration -p:Platform=$env:Platform -p:Version=$v -p:DefineConstants=DEV
```

### 压缩并上传至 OSS

使用 zip 压缩后文件大小大约是 60 MB，使用 7zip 则是 40 MB，用户的网络环境不同，能压缩就尽量压缩。在上传到 OSS 时自定义元数据 `x-oss-meta-version` 为目标版本号，软件可以通过检查这个值判断是否应该更新。

``` PowerShell
# 安装 7zip 压缩模块
Install-Module -Name 7Zip4Powershell -Scope CurrentUser -Force
New-Item -Path ./publish -ItemType Directory -Force
# 压缩等级最大，压缩时包含根目录
Compress-7Zip -ArchiveFileName software.7z -Path $env:Publish_Path -OutputPath ./publish -CompressionLevel Ultra -PreserveDirectoryRoot
# 上传至 OSS
ossutil cp -rf ./publish/* oss://${{ secrets.OSS_BUCKET_NAME }}/software.7z --meta x-oss-meta-version:$v
```

## 自动更新

非打包的 WinUI 项目和 Win32 项目一样，更新时会遇到文件被占用的情况，一般是通过额外的更新进程解决这个问题。不这么一个小东西要啥自行车，用 PowerShell 一把梭，不仅能规避占用问题，还能少一个项目。

我原本是打算在 PowerShell 中下载新版本安装包的，PowerShell 会自动展示下载进度，但是我发现下载速度始终限制在 1 MB/s 左右，而在浏览器或 C# 中下载速度能够达到 6 MB/s。在网上搜了一圈后发现是 **实时显示下载数据量拖慢了性能**，参考 Issue [Progress bar can significantly impact cmdlet performance](https://github.com/PowerShell/PowerShell/issues/2138)，这个问题已在 PowerShell Core 修复，在 Windows 自带的 PowerShell 中仍然存在，唯一的解决办法是禁止显示下载进度。

40 MB 的安装包不显示下载进度还是不太好，只能多写一点代码在软件中下载了（略），下面是 PowerShell 更新脚本代码。

``` PowerShell
# 出现错误时停止执行后续命令，若不加这一句，即使出现错误也无法被 catch
# 代码省略了最外部的 try-catch 部分
$ErrorActionPreference = 'Stop'
# 检查新版本的安装包是否存在
if(![System.IO.File]::Exists('./temp/software.7z')) {
    # 不存在时重新下载
    $null = New-Item "./temp" -ItemType "Directory" -Force
    Invoke-WebRequest -Uri $url -UseBasicParsing -OutFile "./temp/software.7z"
}
# 检查 7zip 解压模块是否存在
if(![System.IO.File]::Exists('./7Zip4Powershell/7Zip4Powershell.psd1')) {
    # 应该使用下面这一句安装解压模块，但是大陆连接源站 PowerShell Gallery 非常困难
    # Install-Module -Name 7Zip4Powershell -Scope CurrentUser -Force
    # 所以需要自行分发解压模块
    Invoke-WebRequest "url/to/7Zip4Powershell.zip" -UseBasicParsing -OutFile "./7Zip4Powershell.zip"
    Expand-Archive -Path "./7Zip4Powershell.zip" -DestinationPath "./" -Force
    Remove-Item -Path "./7Zip4Powershell.zip" -Force -Recurse
}
# 导入模块
Import-Module -Name "./7Zip4Powershell/7Zip4Powershell.psd1" -Force
# 解压
Expand-7Zip -ArchiveFileName "./temp/software.7z" -TargetPath "./temp/"
# 检查软件是否仍在运行
try {
    # 没找到进程时会抛错，需要 catch
    $null = Get-Process -Name "software"
    Write-Host "software.exe 正在运行，等待进程退出" -ForegroundColor Yellow
    Wait-Process -Name "software"
    # 停 1s 等待资源释放
    Start-Sleep -Seconds 1
} catch { }
# 替换文件
Copy-Item -Path "./temp/*" -Destination "./" -Force -Recurse
# 重启软件
Invoke-Item -Path "./software.exe"
# 清理安装包
Remove-Item -Path "./temp" -Force -Recurse
```

就这样，使用 PowerShell 代替了额外的更新进程，也不用考虑更新进程需要更新的问题。唯一需要注意的是手动导入的 7zip 解压模块中的文件会被占用，即使使用 `Remove-Module` 移除模块后，载入的程序集仍不会被卸载，所以不能在安装包中包含该模块，使用前下载最合适，下载后可以一直使用，不用考虑更新问题。

有经验的读者可能会问：哎，系统默认禁用没有签名的 PowerShell 脚本，你总不可能还要让用户手动解除限制吧。这个问题很好解决，不能运行脚本可以运行命令嘛，把脚本内容当作启动参数扔给 PowerShell 进程就好了。

``` cs
// 这里不是脚本文件路径，而是脚本内容
const string script="...";
Process.Start("PowerShell", script);
```

## 最后

单进程软件真的没办法自己更新吗？其实不然，可执行文件在运行期间无法被删除和覆盖，但是可以移动啊。把 exe 和 dll 文件移动到一个待删除文件夹，这个时候就可以把新文件移动到原来的位置。但是还有个问题，非可执行文件被占用后不能移动，所以还是有可能会更新失败，这个方法能用但是不靠谱。

