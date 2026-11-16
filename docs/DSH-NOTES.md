# DSH 环境调试教训（2026-11-16，公司网络）

> 记录一次“拉代码/写文件到处报错”的排查，给其他 DSH 用户参考。
> 结论只代表 **Windows + 公司网络 + DSH workspace-write** 这一组合，家里网络需重测。

## 1. Git 报 schannel 凭据错误

**现象**

```
fatal: unable to access 'https://github.com/...':
schannel: AcquireCredentialsHandle failed: SEC_E_NO_CREDENTIALS (0x8009030E)
```

**根因**：Git for Windows 默认用 schannel；在 DSH 启动的进程 token 下拿不到客户端凭据。网络本身可能是通的。

**解法**

```powershell
git config http.sslBackend openssl
git config http.version HTTP/1.1
```

切 OpenSSL 后 `git ls-remote` 立刻恢复（0.5s）。这条要写进仓库级配置，换机器也要带。

## 2. HTTP/2 + 代理会卡 pack

**现象**：`ls-remote` 能过，但 `clone` / `fetch` 卡在 `Receiving objects`；Watt Toolkit 新版尤其明显，HTTP/2 和 HTTP/1.1 都卡。

**结论**：小请求（ls-remote/info/refs）能过不代表 pack 能过。代理对长连接/大包的缓冲会挂。

**实测（OpenSSL + HTTP/1.1，公司网络）**

| 方式 | `clone --depth=1` |
|---|---|
| `gh-proxy.com` | ✅ **11.2s**，commit 正确 |
| `ghfast.top` | ✅ **21.3s**，commit 正确 |
| 直连 github.com | ❌ 180s+ 卡在 pack |
| `ghproxy.net` | 慢，未完成 |
| `gitclone.com` | 曾成功浅克隆，后 504 |

第三方镜像只用于**公开仓库**，不要带 token/私有库。

## 3. 浅克隆的历史陷阱

- `git fetch --depth=1` 后直接 `merge` 会报 `refusing to merge unrelated histories`
- 必须先 `git fetch --unshallow`，再 merge/reset
- 如果 unshallow 也失败，**重新 clone** 比在浅仓库上反复 deepen 更省时间
- 混用 `--depth` / `--deepen` / `--unshallow` 容易留下 `.git/shallow` 边界，先 `git log` 确认

## 4. DSH 沙箱与 ACL

这次最耗时的不是网络，是“看起来像网络”的权限问题：

- **workspace-write 只保证工作区路径可写**；Python 子进程还会受工作区 ACL 限制。把 clone 放到 `C:\YZ\Unity-npc-demo-latest` 后，Python 写 `server/logs` 直接被拒。
- **`.git` 根目录可能没有当前用户的写权限**：`git fetch` 报 `cannot open .git/FETCH_HEAD: Permission denied`。需要显式 ACL 或 danger-full-access。
- **重命名目录会被进程 cwd 锁住**：先 `working_directory` 切到父目录，再 rename。
- **移动目录会保留原 ACL**；`Move-Item` 进来的目录不一定继承工作区权限，必要时 `icacls` 显式授权。
- **不要用 Python 做批量同步**：权限受限时会在某个文件上中断，留下半同步状态；PowerShell 通常有更高权限。

## 5. 操作教训

1. 先做**小请求**（`ls-remote`）确认 TLS/认证，再做**大请求**（clone）确认 pack。
2. 一次只改一个变量：SSL backend、HTTP 版本、代理、镜像，别同时换。
3. 不要在仓库里再套一个 `latest` clone；两个工作树会让路径、ACL、git 历史全部翻倍。
4. 动 git 前先 `git status`；实验前打快照/分支，失败可回。
5. 记录**耗时和 commit**，否则“感觉快了”没有意义。

## 6. 可用配置（本仓库当前）

```powershell
git config http.sslBackend openssl
git config http.version HTTP/1.1

# fetch 走镜像，push 走直连
git remote set-url origin https://gh-proxy.com/https://github.com/Mew-2/Unity-npc-demo.git
git remote set-url --push origin https://github.com/Mew-2/Unity-npc-demo.git
```

验证：

```powershell
git fetch origin --prune      # 1~2s
git push --dry-run origin main
git status -sb
```

## 7. 如果以后有 SSH key

`ssh.github.com:443` 实测 TCP 0.1s 可连，是比第三方镜像更干净的官方通道：

```powershell
ssh-keygen -t ed25519 -C "you@example.com"
# 公钥加到 GitHub → Settings → SSH and GPG keys
ssh -T -p 443 git@ssh.github.com
git remote set-url origin ssh://git@ssh.github.com:443/Mew-2/Unity-npc-demo.git
```

本次没走通只是因为公司机的 `id_rsa` 没加到 GitHub（`Permission denied (publickey)`），不是网络问题。
