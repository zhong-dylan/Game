FROM golang:1.25-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -o /out/gateway ./cmd/gateway && CGO_ENABLED=0 go build -o /out/gameserver ./cmd/gameserver && CGO_ENABLED=0 go build -o /out/dbinit ./cmd/dbinit

FROM alpine:3.22
RUN apk add --no-cache ca-certificates && adduser -D game
WORKDIR /app
COPY --from=build /out/ /app/
USER game
CMD ["/app/gateway"]
