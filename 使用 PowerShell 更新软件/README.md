# 使用 PowerShell 更新软件

在开发开源小工具的过程中比较头疼的是软件的更新问题。简单来说更新软件分为三个步骤，检查更新、下载安装包、解压覆盖。（不考虑使用 Msix 方式安装的软件）
检查更新是最容易实现的，把最新的版本信息放在一个固定的链接上，让软件定期访问就行，GitHub 提供了 Api 查询 Release 信息，直接拿来用就好。

当确定软件可以更新后，有两条路可以走，一是打开网页，让用户自己下载，如果是解压即用的便携式软件，用户可能还需要自行解压到老版本的文件夹处，这样每多一步都会降低新版本的覆盖率。另一条路就是由软件实现自动更新，但是因为 Windows 的机制，打开的文件无法被删除或覆盖，为了解决这个问题，只能再打包一个独立的更新程序。就像医者不能自医，软件也不能自己更新。

如果不想使用独立的更新程序，那么用 PowerShell 脚本也许是个不错的选择，用系统自带的进程替换独立的更新进程，理论上没有任何问题。整个更新过程如下，假设软件文件名叫 `abc.exe`：

1. 首先是下载安装包，这一步可以放在软件中，也可以放在脚本中，既然标题叫使用 PowerShell 更新软件，那么就在脚本中完成所有步骤。

``` PowerShell
$null = New-Item "./temp" -ItemType "Directory" -Force -ErrorAction Stop
Invoke-WebRequest "url/to/zip/file" -UseBasicParsing -OutFile "./temp/abc_package.zip" -ErrorAction Stop
```

新建一个 `temp` 文件夹，`$null` 是为了不输出文件夹对象，不使用 `-Force` 则可能会报错文件夹已存在，黑底红字看着挺扎眼的。然后从链接下载安装包，保存到临时文件夹中，使用 `-UseBasicParsing` 参数以避免下面这个错误。`-ErrorAction Stop` 这个参数后面再解释。

```
System.NotSupportedException: 无法分析响应内容，因为 Internet Explorer 引擎不可用，或者 Internet Explorer 首次启动配置不完整。请指定 UseBasicParsing 参数，然后再试一次。
```

2. 解压，把压缩包内的文件解压到 `./temp/abc/` 文件夹中。

``` PowerShell
# 压缩包中存在 abc 文件夹
Expand-Archive -Path "./temp/abc_package.zip" -DestinationPath "./temp/" -Force -ErrorAction Stop
# 压缩包中不存在 abc 文件夹
$null = New-Item "./temp/abc" -ItemType "Directory" -Force -ErrorAction Stop
Expand-Archive -Path "./temp/abc_package.zip" -DestinationPath "./temp/abc/" -Force -ErrorAction Stop
```

3. 到这里就可以把软件关掉了。

``` PowerShell
# 直接杀进程
Stop-Process -Name "abc" -ErrorAction Stop
# 或者等用户主动关闭
Wait-Process -Name "abc" -ErrorAction Stop
# 最好再停一会，等资源释放完毕
Start-Sleep -Seconds 1
```

4. 覆盖原文件，最简单的方法是复制所有文件，`-Force` 覆盖原文件，`-Recurse` 递归查找子文件夹。

``` PowerShell
Copy-Item -Path "./temp/abc/*" -Destination "./" -Force -Recurse -ErrorAction Stop
```

5. 清理临时文件夹，打开软件

``` PowerShell
Remove-Item -Path "./temp" -Force -Recurse -ErrorAction Stop
Invoke-Item -Path "./abc.exe" -ErrorAction Stop
```

有经验的读者可能会问：哎，系统默认禁用没有签名的 PowerShell 脚本，你总不可能还要让用户手动解除限制吧。这个问题很好解决，不能运行脚本可以运行命令嘛，把脚本内容当作参数启动 PowerShell 进程就好了。

``` cs
// 这里不是脚本文件路径，而是脚本内容
const string script="...";
Process.Start("PowerShell", script);
```

为什么几乎每一句后面都有 `-ErrorAction Stop`？当 PowerShell 执行遇到错误时，输出完错误后仍会继续执行下一句命令，这怎么能行，这个参数让它遇到错误后停止运行。最后加上 `try catch` 处理错误，完整脚本内容如下。

``` PowerShell
# 字符串中若没有空格，可以去掉引号
try {
    Write-Host "abc.exe 更新脚本"
    Write-Host "当前路径：$(Get-Location)"
    # 新建临时文件夹
    $null = New-Item "./temp" -ItemType "Directory" -Force -ErrorAction Stop
    # 获取下载安装包的大小
    $result = Invoke-WebRequest "url/to/zip/file" -UseBasicParsing -Method HEAD -ErrorAction Stop
    # 参数需要使用单引号
    $size = ($result.Headers['Content-Length']/1024/1024).ToString('F2')
    Write-Host "下载安装包（$size MB）"
    # 下载安装包
    Invoke-WebRequest "url/to/zip/file" -UseBasicParsing -OutFile "./temp/abc_package.zip" -ErrorAction Stop
    Write-Host "开始解压"
    Expand-Archive -Path "./temp/abc_package.zip" -DestinationPath "./temp/" -Force -ErrorAction Stop
    try {
        # 如果没有 abc.exe 进程在运行，会报错，直接跳过下方命令
        $null = Get-Process -Name "abc" -ErrorAction Stop
        Write-Host "abc.exe 正在运行，等待进程退出" -ForegroundColor Yellow
        Wait-Process -Name "abc" -ErrorAction Stop
        Start-Sleep -Seconds 1
    } catch { }
    Write-Host "替换文件"
    Copy-Item -Path "./temp/abc/*" -Destination "./" -Force -Recurse -ErrorAction Stop
    Write-Host "清理安装包"
    Remove-Item -Path "./temp" -Force -Recurse -ErrorAction Stop
    Write-Host "更新完成"
    Invoke-Item -Path "./abc.exe" -ErrorAction Stop
    # 等待 2s 后退出
    Start-Sleep -Seconds 2
} catch {
    # 输出错误
    Write-Host $_.Exception -ForegroundColor Red -BackgroundColor Black
    Write-Host "更新失败，请从以下链接手动下载安装包：" -ForegroundColor Yellow
    Write-Host "url/to/zip/file" -ForegroundColor Yellow
    Write-Host "任意键退出"
    # 防止界面自动关闭
    [Console]::ReadKey()
}
```

单进程软件真的没办法自己更新吗？其实不然，可执行文件在运行期间无法被删除和覆盖，但是可以移动啊。把 exe 和 dll 文件移动到一个待删除文件夹，原来的位置不就空出来了嘛，这个时候就可以把新文件放进来了。但是还有个问题，非可执行文件被占用后不能移动，还是有可能更新失败，所以这个方法能用但是不靠谱。

