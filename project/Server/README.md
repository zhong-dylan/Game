# Game Server

客户端 → Gateway → 后台配置的 Game Server；MySQL 保存管理员和版本路由，Redis 保存后台登录会话与登录限流数据。
后台运行在 Gateway 内，不需要单独启动管理进程。

## 第一次启动

先启动 Docker Desktop，在 Server 目录执行：

```sh
./scripts/init-environment.sh
./scripts/init-db.sh
./scripts/start-gateway.sh
./scripts/start-gameserver.sh 1.0
```

1. 环境脚本交互输入管理员密码，生成 MySQL、Redis 随机密码并写入 `.env`。账户默认 `admin`，文件仅当前用户可读写。
2. 数据库脚本启动 MySQL、Redis，执行 `internal/database/schema.sql`，创建首个管理员。密码保存为 bcrypt 哈希。
3. 网关脚本启动 Gateway，默认监听本机 `8080`。
4. 游戏服务器脚本创建独立容器 `game-1.0`，其版本为 `1.0`，内部监听 `8081`。

打开 **http://localhost:8080/admin/**，用初始化时设置的账户密码登录。
新增配置：版本号 `1.0`，服务器地址 `http://game-1.0:8081`，勾选启用并保存。

```sh
curl -H 'X-Game-Version: 1.0' http://localhost:8080/api/game/info
```

初始化时没有预置版本。没有配置、停用或删除的版本都会被拒绝。

## 增加版本

```sh
./scripts/start-gameserver.sh 2.0
```

后台添加版本 `2.0`，服务器地址 `http://game-2.0:8081`。保存立即生效，无需修改网关文件或重启网关。
如果 Game Server 在其他机器上，填写 Gateway 可访问的实际 HTTP/HTTPS 地址。
路由版本必须与 Game Server 的 `GAME_VERSION` 一致；它还需要与客户端 GameConfig 中的版本号 一致。
管理后台只管理版本路由，不创建或停止游戏进程；进程由启动脚本或外部部署工具管理。

同一程序目前作为多个独立版本进程运行。不同版本有独立业务代码时，可以分别构建部署对应版本程序，再在后台配置地址。

## 配置

`.env` 可配置：

- `MYSQL_ROOT_PASSWORD`、`MYSQL_PASSWORD`：数据库密码。
- `REDIS_PASSWORD`：Redis 密码。
- `ADMIN_USERNAME`、`ADMIN_PASSWORD`：首次初始化管理员账户密码。
- `GATEWAY_BIND`、`GATEWAY_PORT`：网关对外地址和端口，默认 `127.0.0.1:8080`。
- `ADMIN_COOKIE_SECURE`：默认 `false` 供本机 HTTP；通过 HTTPS 部署时设置为 `true`。

非交互环境可提供 `ADMIN_USERNAME`、`ADMIN_PASSWORD` 环境变量后运行 `init-environment.sh`。
管理员密码需 12–72 字节，不含单引号、反斜杠或换行。脚本不会打印密码，不会覆盖现有 `.env`。
已有 MySQL 数据卷时，`.env` 中的数据库密码必须与数据卷中已有用户密码一致；更改环境变量不会修改数据库内的用户密码。
沿用原 Compose 项目名 `server`，保留已有命名数据卷。

MySQL 与 Redis 不发布宿主机端口；所有容器在同一 Compose 网络中访问它们。
游戏服务使用固定的容器内部端口 `8081`，不同容器不冲突。

## 数据初始化与持久化

`init-db.sh` 可以重复执行，创建缺失的表和管理员，但不重置已有账户密码，不覆盖版本路由，不清空 Redis。
`start-gateway.sh` 也会通过一次性 `db-init` 服务确保初始化完成。
管理员、版本路由存入 MySQL 命名卷，Redis 会话使用 AOF 持久化和 8 小时过期时间。
MySQL 和 Redis 都是 Gateway 的必要依赖；依赖不可用时返回错误，不回退到旧配置。

常驻进程为 MySQL、Redis、Gateway，以及手动启动的各个 Game Server。`db-init` 完成后退出。

```sh
docker compose ps -a
docker compose logs -f gateway
docker logs -f game-1.0
# 停止一个游戏服务，路由配置保留，可在后台停用它：
docker stop game-1.0
# 再次启动原有容器：
docker start game-1.0
```

再次执行 `start-gameserver.sh` 使用同一版本时，已有容器名会导致失败，不会替换或删除现有服务。

## 接口

- `GET /healthz`：网关自身及 MySQL/Redis 健康检查。
- `GET /api/game/info`：经版本路由访问 Game Server。
- `/api/game/` 下所有路径（包括未来的玩家登录接口）先验证版本路由。
- 缺少/无效/重复版本头：400；未配置或停用版本：426；数据库不可用：503；后端不可用：502。
- 管理后台账户登录不需要游戏版本头，与玩家登录独立。
- 当前未实现玩家账户注册与登录业务；网关已经对整个游戏 API 路径实施版本准入。

管理 API：

- `POST /admin/api/login`：JSON 账户密码登录，返回 CSRF token，并设置 HttpOnly 会话 Cookie。
- `GET /admin/api/session`：查询会话。
- `POST /admin/api/logout`：销毁会话。
- `GET /admin/api/routes`：列出版本。
- `PUT /admin/api/routes`：新增/编辑 `{ "version": "1.0", "server_url": "http://game-1.0:8081", "enabled": true }`。
- `DELETE /admin/api/routes/{version}`：删除版本。

配置操作与退出要求 Cookie 和 `X-CSRF-Token`，页面自动处理。
登录有每 IP 每分钟 10 次的限制。会话到期、退出登录、Redis 不可用后不能继续配置。

## Unity

GameLaunch 的 Launch Config 下拉框选择 DebugLaunchConfig 或 ReleaseLaunchConfig，Server URL 保存在所选 ScriptableObject 资源中。
Debug 默认 `http://localhost:8080`，Release 填写实际网关地址。
客户端版本来自 `Assets/Scripts/Game/Config/GameConfig.cs` 的 GameVersion，分辨率默认 1080×1920。
GameLaunch 启动时仍只实例化 AddressablesMgr，不主动联网。

```csharp
using (var request = gameLaunch.CreateServerRequest("/api/game/info"))
{
    yield return request.SendWebRequest();
    Debug.Log(request.downloadHandler.text);
}
```

## 验证

```sh
go test -race ./...
go vet ./...
bash -n scripts/*.sh
docker compose config -q
```

测试覆盖后台鉴权、CSRF、登录错误和限流、Redis 会话过期/退出/轮换、配置变更即时生效、未配置版本拒绝、依赖故障和数据库初始化幂等行为。
