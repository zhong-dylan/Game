# Nakama Binary + CockroachDB

该目录改为 `Nakama 二进制包 + CockroachDB 二进制` 的本地启动方式，不再依赖 Docker。

## 目录要求

- 当前已存在 Nakama 二进制目录，例如 `nakama-3.38.0-darwin-amd64/`
- 还需要把 CockroachDB 二进制解压到 `Server` 目录下，脚本会自动查找名为 `cockroach` 的可执行文件

如果自动识别不到，也可以在 `.env` 中手动指定：

```bash
NAKAMA_BINARY=/absolute/path/to/nakama
COCKROACH_BINARY=/absolute/path/to/cockroach
```

## 脚本

- `scripts/install-cockroach.sh`：下载并安装 CockroachDB 二进制
- `scripts/start.sh`：启动 CockroachDB，执行 Nakama 迁移，再启动 Nakama
- `scripts/stop.sh`：停止 CockroachDB 和 Nakama
- `scripts/restart.sh`：重启服务
- `scripts/logs.sh`：查看日志

## 首次使用

1. 确认 `Server` 下存在 `nakama` 二进制。
2. 下载并安装 CockroachDB：

```bash
./scripts/install-cockroach.sh
```

当前脚本默认使用 `COCKROACH_VERSION=25.1.10`，并按机器架构自动选择 macOS Intel 或 macOS ARM64 包。
3. 复制环境变量文件：

```bash
cp .env.example .env
```

4. 按需修改 `.env`。
5. 启动：

```bash
./scripts/start.sh
```

## 默认地址

- CockroachDB SQL: `127.0.0.1:26257`
- CockroachDB Admin UI: `http://127.0.0.1:8080`
- Nakama HTTP API: `http://127.0.0.1:7350`
- Nakama gRPC API: `127.0.0.1:7349`
- Nakama Console: `http://127.0.0.1:7351`

## 常用命令

```bash
./scripts/start.sh
./scripts/stop.sh
./scripts/restart.sh
./scripts/logs.sh
./scripts/logs.sh cockroach
./scripts/logs.sh nakama
```

## 数据和日志

- CockroachDB 数据目录：`Server/data/cockroach`
- Nakama 数据目录：`Server/data/nakama`
- 日志目录：`Server/logs`
- PID 目录：`Server/run`

## 说明

- 启动脚本会先启动 CockroachDB，再执行 `nakama migrate up`。
- `NAKAMA_DATABASE_ADDRESS` 默认使用 `root@127.0.0.1:26257`。
- 当前脚本按本机开发环境设计，`COCKROACH_INSECURE=true`，不适合生产环境。
- `install-cockroach.sh` 的下载地址使用 Cockroach Labs 官方二进制域名 `binaries.cockroachdb.com`。
