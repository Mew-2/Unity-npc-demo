# DSH 网络/权限排查（只写方法）

> 本机耗时、镜像快慢、代理配置**一律不入库**；换机器/网络/工具版本后重新测。

## 先分层，再换变量

1. **TLS/认证**：`git ls-remote` 能过，说明域名和凭据没问题
2. **pack 传输**：`clone --depth=1` 能过，才算网络可用
3. **文件权限**：单独测写文件，别和网络混在一起

一次只改一个变量：SSL backend、HTTP 版本、代理、镜像。

## 工具事实（与具体网络无关）

- Git for Windows 默认 schannel，少数环境报 `SEC_E_NO_CREDENTIALS`；可试 `http.sslBackend=openssl`
- HTTP/2 配合某些本地代理会卡 `Receiving objects`；可试 `http.version=HTTP/1.1`
- 浅克隆不要直接 `merge`，会报 unrelated histories；先 `git fetch --unshallow`
- 第三方镜像只用于公开仓库，不要带 token/私有库
- 官方 SSH 443（`ssh.github.com:443`）是更干净的通道，前提是公钥已加到 GitHub

## DSH 沙箱常见坑

- workspace-write 只保证工作区路径可写；**子进程仍可能受工作区 ACL 限制**
- `.git` 根目录可能没有当前用户写权限，`git fetch` 会报 `.git/FETCH_HEAD: Permission denied`
- 目录被进程 cwd 占用时 rename 会失败；先切到父目录
- 移动进来的目录会保留原 ACL，不一定继承工作区权限
- 不要用 Python 批量同步：权限中断会留下半同步状态

## 写不写进 HANDOFF

换机器、换网络、换工具版本，任一会变 → **不写进 HANDOFF**，只留本地记录。

HANDOFF 只保留：当前状态、必须带什么、怎么跑、下一步。
