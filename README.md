# 重庆三峡科技大学校园网自动登录

重庆三峡科技大学校园网只能连接一个设备，每次开电脑的时候都因为之前连接过其他设备要重新认证。使用此工具，开机检测到校园网未认证时，自动向认证服务器发送登录请求，不需要打开浏览器手动登录。

## 使用方法

1. 双击 `CampusNetAutoLogin.exe`，打开中文控制窗口；
2. 填写自己的校园网账号和密码；
3. 点“保存账号密码”；
4. 点“立即登录测试”确认可以登录；
5. 点“开启开机自启”，以后每次开机登录 Windows 都会自动登录校园网。

## 关闭自动登录

- 双击 exe → 点“关闭开机自启”；或
- 打开启动文件夹（`Win+R` → `shell:startup`）删除其中的 exe；或
- 在任务管理器“启动应用”中禁用。

## 工作原理

程序直接复现校园网 Portal 登录请求：

```
GET http://1.1.1.1:801/eportal/portal/login
    ?callback=dr1003
    &login_method=1
    &user_account=,0,账号
    &user_password=密码
    &wlan_user_ip=本机IPv4
    &jsVersion=4.2.1
```

放在 Windows“启动”文件夹里时静默运行；直接双击 exe 时打开中文开关界面。

## 自行编译

环境：Windows + .NET Framework 4.x（Windows 自带）。

```bat
BuildExe.cmd
```

## 安全说明

账号密码会以明文保存在 exe 同目录的 `CampusNetAutoLogin.ini`。不要把你的账号密码文件分享给别人。

> 本项目仅供学习交流，请遵守校园网使用规定。
