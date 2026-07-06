# RobustDownloader

[English](README.en.md)

RobustDownloader 是一个基于 .NET、Avalonia 和 ShadUI 的跨平台桌面下载管理器。它重点面向大文件下载场景，提供队列管理、可续传分段下载、站点凭据、CRC64 工作流，以及清晰的工具型界面。

## 界面预览

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="screens/SS1_dark.png">
  <source media="(prefers-color-scheme: light)" srcset="screens/SS1.png">
  <img alt="RobustDownloader 界面预览" src="screens/SS1.png">
</picture>

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

## 下载原理

RobustDownloader 会先使用 HTTP `Range` 请求探测服务器能力。服务器支持分段响应时，软件会把目标文件拆成可配置大小的分块，并由多个下载 worker 并行拉取这些分块。任务运行时会保留 `.downloading` 临时文件和一个很小的 `.cfg` 续传标记，因此停止任务后，只要服务器仍然提供兼容的 Range 语义，就可以从最后一次安全写入的位置继续下载。

已下载的分块不会直接按完成顺序随机写入磁盘，而是先进入内存缓冲区，再由单独的写入流程按照文件偏移顺序写入目标文件。这个设计是有意用更高一些的内存占用换取更可预测的磁盘 I/O 和更稳妥的续传行为。

为什么内存占用可能看起来偏高：

- 每个正在运行的任务可能同时持有多个已完成或正在处理的分块。
- 内存上限主要受分块大小、线程数和全局并发数影响。
- 较大的分块可以减少调度开销，并可能提升吞吐，但也会增加缓冲区占用。
- 如果希望降低内存占用，可以调小分块大小、线程数或全局并发数。

为什么对机械硬盘更友好：

- 网络下载 worker 可以乱序完成分块，但磁盘写入会按文件顺序串行提交。
- 顺序写入可以减少机械硬盘上代价较高的随机寻道。
- 当已知文件总大小时，软件会预先分配 `.downloading` 文件，减少下载过程中的反复扩容。
- 写盘 flush 会批量进行，而不是每次小块网络读取后都强制同步磁盘。

如果服务器不支持 `Range`，RobustDownloader 会自动退回单线程顺序流式下载。这个模式兼容性更好，但无法提供同等可靠的分段续传能力。

## 项目结构

- `src/RobustDownloader.sln`：解决方案文件
- `src/RobustDownloader/`：应用源码、资源文件和项目文件
- `README.md`、`README.en.md`、`LICENSE`：项目文档和许可证

## 数据文件

RobustDownloader 会把任务和设置保存到用户应用数据目录：

- `tasks.json`：任务列表
- `settings.json`：应用设置

具体路径可以在 **设置 > 数据文件** 中查看。

## 说明

RobustDownloader 会在需要时保留下载临时状态，以便停止后的任务尽可能安全续传。实际可续传能力仍取决于服务器行为：部分站点不支持 Range 请求，或不提供可靠的续传语义。

## 许可证

MIT License。详见 [LICENSE](LICENSE)。
