# RobustDownloader

[English](README.md)

RobustDownloader 是一个基于 .NET、Avalonia 和 ShadUI 的跨平台桌面下载管理器。它重点面向大文件下载场景，提供队列管理、可续传分段下载、站点凭据、CRC64 工作流，以及清晰的工具型界面。

## 作者声明

本项目代码完全由 AI 实现。人工仅提供产品方向、设计思路、需求描述和评审反馈。

## 功能

- 多任务下载队列，支持开始、停止、删除、全部开始和全部停止
- 可续传的分段下载，支持配置线程数和分块大小
- 全局并发控制
- 支持持久化任务列表范围：最近 50 条、最近 100 条、最近 200 条或全部任务
- 当服务器不支持 Range 请求时，自动切换为单线程顺序下载
- 支持粘贴多行 URL 批量创建任务
- 支持任务级自定义 HTTP Header，以及全局默认 Header
- 支持 HTTP 代理设置：跟随系统、不使用代理或手动填写代理地址
- 支持站点匹配规则和 Basic Auth 用户名/密码
- 支持仅提取 CRC64 或跳过 CRC64 提取
- 支持使用服务器时间更新文件时间
- 使用本地 JSON 持久化任务和设置
- 支持动态语言切换：自动、英文、简体中文、繁体中文
- 支持主题设置：跟随系统、浅色、深色
- 支持可折叠的任务详情面板，展示日志、Header、诊断和运行模式
- 标题栏展示来自项目元数据的应用版本号

## 环境要求

- 与项目目标框架兼容的 .NET SDK
- macOS、Windows 或 Linux 桌面环境

## 构建和运行

```bash
dotnet restore src/RobustDownloader.sln
dotnet run --project src/RobustDownloader/RobustDownloader.csproj
```

仅构建：

```bash
dotnet build src/RobustDownloader.sln
```

## 设置项

在主工具栏打开 **设置**，可以配置：

- 外观：语言和主题
- 下载参数：默认线程数、分块大小、并发、CRC64 行为、文件时间行为
- 网络：HTTP 代理模式和手动代理地址
- 保存与 Header：默认保存目录和默认 HTTP Header
- 站点凭据：站点匹配规则、用户名和密码
- 数据文件：任务列表 JSON 和设置项 JSON 的路径

## 项目结构

- `src/RobustDownloader.sln`：解决方案文件
- `src/RobustDownloader/`：应用源码、资源文件和项目文件
- `README.md`、`README.zh-CN.md`、`LICENSE`：项目文档和许可证

## 数据文件

RobustDownloader 会把任务和设置保存到用户应用数据目录：

- `tasks.json`：任务列表
- `settings.json`：应用设置

具体路径可以在 **设置 > 数据文件** 中查看。

## 说明

RobustDownloader 会在需要时保留下载临时状态，以便停止后的任务尽可能安全续传。实际可续传能力仍取决于服务器行为：部分站点不支持 Range 请求，或不提供可靠的续传语义。

## 许可证

MIT License。详见 [LICENSE](LICENSE)。
