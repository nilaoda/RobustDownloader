using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using RobustDownloader.Models;

namespace RobustDownloader.Services;

public static class LocalizationService
{
    private static AppLanguageMode _mode = AppLanguageMode.Auto;

    public static event EventHandler? LanguageChanged;

    public static AppLanguageMode Mode => _mode;
    public static AppLanguageMode EffectiveMode { get; private set; } = Resolve(AppLanguageMode.Auto);

    public static void Apply(AppLanguageMode mode)
    {
        _mode = mode;
        EffectiveMode = Resolve(mode);
        var resources = GetResources(EffectiveMode);

        if (Application.Current != null)
        {
            foreach (var (key, value) in resources)
                Application.Current.Resources[key] = value;
        }

        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key)
    {
        var resources = GetResources(EffectiveMode);
        return resources.TryGetValue(key, out var value) ? value : key;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    public static bool IsLocalizedValue(string key, string value)
    {
        return string.Equals(value, key, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(ZhHans, key), value, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(En, key), value, StringComparison.Ordinal) ||
               string.Equals(GetResourceValue(ZhHant, key), value, StringComparison.Ordinal);
    }

    public static IEnumerable<string> GetLocalizedValues(string key)
    {
        yield return GetResourceValue(ZhHans, key);
        yield return GetResourceValue(En, key);
        yield return GetResourceValue(ZhHant, key);
    }

    private static string GetResourceValue(IReadOnlyDictionary<string, string> resources, string key)
    {
        return resources.TryGetValue(key, out var value) ? value : key;
    }

    private static AppLanguageMode Resolve(AppLanguageMode mode)
    {
        if (mode != AppLanguageMode.Auto) return mode;

        foreach (var name in GetPreferredLanguageNames())
        {
            var resolved = ResolveLanguageName(name);
            if (resolved.HasValue)
                return resolved.Value;
        }

        return AppLanguageMode.English;
    }

    private static AppLanguageMode? ResolveLanguageName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        name = NormalizeLanguageName(name);
        if (name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-TW", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-HK", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("-MO", StringComparison.OrdinalIgnoreCase))
            return AppLanguageMode.TraditionalChinese;

        if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return AppLanguageMode.SimplifiedChinese;

        return name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? AppLanguageMode.English
            : null;
    }

    private static IEnumerable<string> GetPreferredLanguageNames()
    {
        if (OperatingSystem.IsMacOS())
        {
            foreach (var name in ReadMacOSAppleLanguages())
                yield return name;
        }

        yield return CultureInfo.CurrentUICulture.Name;
        yield return CultureInfo.CurrentCulture.Name;

        foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            yield return Environment.GetEnvironmentVariable(variable) ?? "";
    }

    private static IReadOnlyList<string> ReadMacOSAppleLanguages()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/defaults",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            startInfo.ArgumentList.Add("read");
            startInfo.ArgumentList.Add("-g");
            startInfo.ArgumentList.Add("AppleLanguages");

            using var process = Process.Start(startInfo);
            if (process == null || !process.WaitForExit(1000))
                return [];

            var output = process.StandardOutput.ReadToEnd();
            var names = new List<string>();
            foreach (var line in output.Split('\n'))
            {
                var name = line.Trim().Trim(',', '"');
                if (name.Length > 0 && name is not "(" and not ")")
                    names.Add(name);
            }

            return names;
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeLanguageName(string name)
    {
        var normalized = name.Trim().Trim('"').Replace('_', '-');
        var dotIndex = normalized.IndexOf('.', StringComparison.Ordinal);
        if (dotIndex >= 0)
            normalized = normalized[..dotIndex];

        return normalized;
    }

    private static IReadOnlyDictionary<string, string> GetResources(AppLanguageMode mode)
    {
        return mode switch
        {
            AppLanguageMode.English => En,
            AppLanguageMode.TraditionalChinese => ZhHant,
            _ => ZhHans
        };
    }

    private static readonly IReadOnlyDictionary<string, string> ZhHans = new Dictionary<string, string>
    {
        ["App.Name"] = "RobustDownloader",
        ["Main.NewTask"] = "新建任务",
        ["Main.Start"] = "开始",
        ["Main.Stop"] = "停止",
        ["Main.Delete"] = "删除",
        ["Main.GlobalSpeed"] = "全局速度",
        ["Main.Concurrency"] = "并发",
        ["Main.StartAll"] = "全部开始",
        ["Main.StopAll"] = "全部停止",
        ["Main.Settings"] = "设置",
        ["Main.Queue"] = "下载队列",
        ["Main.Recent50"] = "最近50条",
        ["Main.Recent100"] = "最近100条",
        ["Main.Recent200"] = "最近200条",
        ["Main.AllTasks"] = "全部任务",
        ["Main.Details"] = "任务详情",
        ["Main.ShowDetails"] = "显示详情",
        ["Main.HideDetails"] = "隐藏详情",
        ["Main.CollapseDetails"] = "收起",
        ["Main.EmptyTitle"] = "还没有下载任务",
        ["Main.EmptyBody"] = "添加一个 URL 或批量粘贴多行链接后，队列会显示下载进度、速度和状态。",
        ["Main.SelectTaskTitle"] = "选择一个任务",
        ["Main.SelectTaskBody"] = "右侧会显示保存位置、日志、Header 和诊断信息。",
        ["Main.CopyUrl"] = "复制链接",
        ["Main.CopyFileName"] = "复制文件名",
        ["Main.CopySavePath"] = "复制保存路径",
        ["Main.CopyPath"] = "复制路径",
        ["Main.OpenFolder"] = "打开文件夹",
        ["Main.Overview"] = "概览",
        ["Main.Log"] = "日志",
        ["Main.Headers"] = "Header",
        ["Main.RecentLog"] = "最近日志",
        ["Main.Diagnostics"] = "诊断",
        ["Diagnostic.None"] = "无异常",
        ["Diagnostic.Initializing"] = "初始化中",
        ["Diagnostic.NoRangeSupport"] = "服务器不支持 Range",
        ["Diagnostic.DiskBottleneck"] = "磁盘写入可能受限",
        ["Diagnostic.BufferDeadlock"] = "缓冲区阻塞，正在尝试恢复",
        ["Diagnostic.NetworkHang"] = "网络连接可能卡住",
        ["Diagnostic.Idle"] = "内部队列空闲",
        ["Diagnostic.Completed"] = "已完成",
        ["Diagnostic.Error"] = "错误",
        ["Main.SettingsSaved"] = "设置已保存",
        ["Main.OpenedFolder"] = "已打开保存位置",
        ["Main.OpenFolderFailed"] = "打开文件夹失败: {0}",
        ["Main.Copied"] = "已复制到剪贴板",
        ["Queue.Empty"] = "队列空闲 | 无任务",
        ["Queue.Ready"] = "就绪",
        ["Queue.Idle"] = "队列空闲 | 排队: {0} | 总任务: {1}",
        ["Queue.Running"] = "下载中: {0} | 并发上限: {1} | 排队: {2} | 总任务: {3}",
        ["Queue.RecentInfo"] = "显示最近 {0} / {1} 条",
        ["Queue.AllInfo"] = "显示全部 {0} 条任务",
        ["Column.FileName"] = "文件名",
        ["Column.Status"] = "状态",
        ["Column.Progress"] = "进度",
        ["Column.Speed"] = "速度",
        ["Column.Eta"] = "剩余",
        ["Eta.Completed"] = "完成",
        ["Eta.Exists"] = "已存在",
        ["Column.Size"] = "大小",
        ["Column.Mode"] = "模式",
        ["Field.SavePath"] = "保存路径",
        ["Field.Url"] = "URL",
        ["Field.Threads"] = "线程",
        ["Field.BlockSizeMb"] = "分块 MB",
        ["Field.Mode"] = "运行模式",
        ["Field.FileTime"] = "文件时间",
        ["Task.Status.Pending"] = "排队",
        ["Task.Status.Running"] = "下载中",
        ["Task.Status.Paused"] = "已暂停",
        ["Task.Status.Completed"] = "完成",
        ["Task.Status.Error"] = "错误",
        ["Task.Status.Stopped"] = "已停止",
        ["Task.FileTime.Server"] = "使用服务器时间",
        ["Task.FileTime.Local"] = "保留本地完成时间",
        ["Add.Title"] = "新建下载任务",
        ["Add.Description"] = "粘贴一个或多个 URL，确认保存目录后加入队列。",
        ["Add.Urls"] = "下载地址",
        ["Add.UrlsHint"] = "一行一个 URL。单个 URL 可手动覆盖文件名，批量任务会自动识别文件名。",
        ["Add.SaveDirectory"] = "保存目录",
        ["Add.Browse"] = "选择",
        ["Add.FileName"] = "文件名",
        ["Add.FileNameHint"] = "仅在输入单个 URL 时可编辑。",
        ["Add.Advanced"] = "高级选项",
        ["Add.CustomHeaders"] = "自定义 HTTP Header",
        ["Add.UpdateTimestamp"] = "使用服务器时间修改文件时间",
        ["Add.CrcOnly"] = "仅提取 CRC64",
        ["Add.SkipCrc"] = "跳过 CRC64 提取",
        ["Add.Cancel"] = "取消",
        ["Add.Enqueue"] = "加入队列",
        ["Add.BatchFileName"] = "(批量任务 - 自动生成)",
        ["Validation.EnterUrl"] = "请输入 URL。",
        ["Validation.SelectDirectory"] = "请选择保存目录。",
        ["Validation.DirectoryMissing"] = "保存目录不存在。",
        ["Validation.SelectDirectoryFailed"] = "选择目录失败：{0}",
        ["Validation.Threads"] = "线程数必须是大于 0 的整数。",
        ["Validation.BlockSize"] = "分块大小必须是大于 0 的数字。",
        ["Validation.CrcConflict"] = "仅提取 CRC64 不能与跳过 CRC64 同时启用。",
        ["Settings.Title"] = "全局设置",
        ["Settings.Description"] = "这些默认值会用于新建任务；站点凭据会在匹配 URL 时自动携带 Basic Auth。",
        ["Settings.Appearance"] = "外观",
        ["Settings.Language"] = "语言",
        ["Settings.LanguageHint"] = "自动会根据系统语言选择简体中文、繁体中文或英文。",
        ["Settings.Theme"] = "主题",
        ["Settings.ThemeHint"] = "跟随系统会使用操作系统当前的浅色或深色模式。",
        ["Settings.Auto"] = "自动",
        ["Settings.LanguageEn"] = "English",
        ["Settings.LanguageZhHans"] = "简体中文",
        ["Settings.LanguageZhHant"] = "繁體中文",
        ["Settings.ThemeSystem"] = "跟随系统",
        ["Settings.ThemeLight"] = "浅色",
        ["Settings.ThemeDark"] = "深色",
        ["Settings.Download"] = "下载参数",
        ["Settings.DownloadDefaults"] = "默认下载参数",
        ["Settings.DefaultThreads"] = "默认线程",
        ["Settings.DefaultBlock"] = "默认分块 MB",
        ["Settings.DefaultConcurrency"] = "默认并发",
        ["Settings.DefaultBehavior"] = "默认任务行为",
        ["Settings.DefaultUpdateTimestamp"] = "默认使用服务器时间修改文件时间",
        ["Settings.DefaultSkipCrc"] = "默认跳过 CRC64 提取",
        ["Settings.Network"] = "网络",
        ["Settings.Proxy"] = "HTTP 代理",
        ["Settings.ProxySystem"] = "跟随系统",
        ["Settings.ProxyDisabled"] = "不使用代理",
        ["Settings.ProxyManual"] = "手动填写",
        ["Settings.ProxyHint"] = "手动模式请填写完整代理地址，例如 http://127.0.0.1:7890。",
        ["Settings.SaveAndHeaders"] = "保存与 Header",
        ["Settings.DefaultSaveDirectory"] = "默认保存目录",
        ["Settings.DefaultSaveDirectoryHint"] = "使用上一次目录时，每次成功加入任务后会记住当次保存目录；固定目录默认使用系统下载目录。",
        ["Settings.SaveDirectoryLastUsed"] = "使用上一次的下载目录",
        ["Settings.SaveDirectoryFixed"] = "使用固定目录",
        ["Settings.DefaultHeaders"] = "默认 HTTP Header",
        ["Settings.Credentials"] = "站点凭据",
        ["Settings.CredentialsTitle"] = "站点用户名/密码",
        ["Settings.Add"] = "添加",
        ["Settings.Delete"] = "删除",
        ["Settings.CredentialsHint"] = "匹配可以填写 example.com、*.example.com 或 https://example.com/path。URL 内已有用户名密码或 Header 已含 Authorization 时不会覆盖。",
        ["Settings.Enabled"] = "启用",
        ["Settings.Match"] = "匹配",
        ["Settings.Username"] = "用户名",
        ["Settings.Password"] = "密码",
        ["Settings.DataFiles"] = "数据文件",
        ["Settings.LocalDataFiles"] = "本地数据文件",
        ["Settings.DataFilesHint"] = "这些路径用于排查、备份或迁移配置。文件内容仍由应用自动维护。",
        ["Settings.TasksJson"] = "任务列表 JSON",
        ["Settings.SettingsJson"] = "设置项 JSON",
        ["Settings.Save"] = "保存",
        ["Settings.Cancel"] = "取消",
        ["Validation.DefaultThreads"] = "默认线程必须大于 0。",
        ["Validation.DefaultBlock"] = "默认分块必须大于 0。",
        ["Validation.DefaultConcurrency"] = "默认并发必须是 1、2、3、5、8。",
        ["Validation.DefaultDirectoryMissing"] = "默认保存目录不存在。",
        ["Validation.ProxyAddress"] = "手动代理地址必须是完整地址，例如 http://127.0.0.1:7890。",
        ["Validation.CredentialPattern"] = "站点凭据的匹配规则不能为空。",
        ["Dialog.SelectSaveDirectory"] = "选择保存目录",
        ["Dialog.SelectDefaultSaveDirectory"] = "选择默认保存目录",
        ["TaskLog.Added"] = "已加入任务列表",
        ["TaskLog.Queued"] = "已加入队列",
        ["TaskLog.Started"] = "开始下载",
        ["TaskLog.Stopping"] = "正在停止，等待安全写盘点...",
        ["TaskLog.Removed"] = "已从队列移除",
        ["Download.SkippedExisting"] = "目标文件已存在，已跳过",
        ["Download.CrcDone"] = "CRC64 提取完成",
        ["Download.RangeFallback"] = "服务器不支持 Range，已切换为单线程顺序下载；该模式无法可靠断点续传。",
        ["Download.CanceledWithResume"] = "用户已停止下载。已保留可安全续传的临时数据。",
        ["Download.Canceled"] = "用户已停止下载",
        ["Download.CompletedAverageSpeed"] = "下载完成，平均速度 {0}/s",
        ["Download.SingleThreadCompleted"] = "单线程下载完成",
        ["Error.TasksJsonLoadFailed"] = "tasks.json 读取失败",
        ["Error.SettingsJsonLoadFailed"] = "settings.json 读取失败，已使用默认设置"
    };

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["App.Name"] = "RobustDownloader",
        ["Main.NewTask"] = "New Task",
        ["Main.Start"] = "Start",
        ["Main.Stop"] = "Stop",
        ["Main.Delete"] = "Delete",
        ["Main.GlobalSpeed"] = "Global speed",
        ["Main.Concurrency"] = "Concurrent",
        ["Main.StartAll"] = "Start All",
        ["Main.StopAll"] = "Stop All",
        ["Main.Settings"] = "Settings",
        ["Main.Queue"] = "Download Queue",
        ["Main.Recent50"] = "Recent 50",
        ["Main.Recent100"] = "Recent 100",
        ["Main.Recent200"] = "Recent 200",
        ["Main.AllTasks"] = "All Tasks",
        ["Main.Details"] = "Task Details",
        ["Main.ShowDetails"] = "Show Details",
        ["Main.HideDetails"] = "Hide Details",
        ["Main.CollapseDetails"] = "Collapse",
        ["Main.EmptyTitle"] = "No download tasks yet",
        ["Main.EmptyBody"] = "Add one URL or paste multiple links. The queue will show progress, speed, and status.",
        ["Main.SelectTaskTitle"] = "Select a task",
        ["Main.SelectTaskBody"] = "Save path, logs, headers, and diagnostics will appear here.",
        ["Main.CopyUrl"] = "Copy URL",
        ["Main.CopyFileName"] = "Copy File Name",
        ["Main.CopySavePath"] = "Copy Save Path",
        ["Main.CopyPath"] = "Copy Path",
        ["Main.OpenFolder"] = "Open Folder",
        ["Main.Overview"] = "Overview",
        ["Main.Log"] = "Log",
        ["Main.Headers"] = "Header",
        ["Main.RecentLog"] = "Recent Log",
        ["Main.Diagnostics"] = "Diagnostics",
        ["Diagnostic.None"] = "No issues",
        ["Diagnostic.Initializing"] = "Initializing",
        ["Diagnostic.NoRangeSupport"] = "Server does not support Range",
        ["Diagnostic.DiskBottleneck"] = "Disk write may be bottlenecked",
        ["Diagnostic.BufferDeadlock"] = "Buffer is blocked; recovery is in progress",
        ["Diagnostic.NetworkHang"] = "Network connection may be stalled",
        ["Diagnostic.Idle"] = "Internal queue is idle",
        ["Diagnostic.Completed"] = "Completed",
        ["Diagnostic.Error"] = "Error",
        ["Main.SettingsSaved"] = "Settings saved",
        ["Main.OpenedFolder"] = "Save location opened",
        ["Main.OpenFolderFailed"] = "Failed to open folder: {0}",
        ["Main.Copied"] = "Copied to clipboard",
        ["Queue.Empty"] = "Queue idle | No tasks",
        ["Queue.Ready"] = "Ready",
        ["Queue.Idle"] = "Queue idle | Pending: {0} | Total: {1}",
        ["Queue.Running"] = "Downloading: {0} | Limit: {1} | Pending: {2} | Total: {3}",
        ["Queue.RecentInfo"] = "Showing recent {0} / {1}",
        ["Queue.AllInfo"] = "Showing all {0} tasks",
        ["Column.FileName"] = "File Name",
        ["Column.Status"] = "Status",
        ["Column.Progress"] = "Progress",
        ["Column.Speed"] = "Speed",
        ["Column.Eta"] = "ETA",
        ["Eta.Completed"] = "Done",
        ["Eta.Exists"] = "Exists",
        ["Column.Size"] = "Size",
        ["Column.Mode"] = "Mode",
        ["Field.SavePath"] = "Save Path",
        ["Field.Url"] = "URL",
        ["Field.Threads"] = "Threads",
        ["Field.BlockSizeMb"] = "Block MB",
        ["Field.Mode"] = "Run Mode",
        ["Field.FileTime"] = "File Time",
        ["Task.Status.Pending"] = "Pending",
        ["Task.Status.Running"] = "Running",
        ["Task.Status.Paused"] = "Paused",
        ["Task.Status.Completed"] = "Done",
        ["Task.Status.Error"] = "Error",
        ["Task.Status.Stopped"] = "Stopped",
        ["Task.FileTime.Server"] = "Use server time",
        ["Task.FileTime.Local"] = "Use local completion time",
        ["Add.Title"] = "New Download Task",
        ["Add.Description"] = "Paste one or more URLs, choose a save directory, then add them to the queue.",
        ["Add.Urls"] = "Download URLs",
        ["Add.UrlsHint"] = "One URL per line. A single URL can override the file name; batch tasks infer names automatically.",
        ["Add.SaveDirectory"] = "Save Directory",
        ["Add.Browse"] = "Browse",
        ["Add.FileName"] = "File Name",
        ["Add.FileNameHint"] = "Editable only when a single URL is entered.",
        ["Add.Advanced"] = "Advanced Options",
        ["Add.CustomHeaders"] = "Custom HTTP Headers",
        ["Add.UpdateTimestamp"] = "Use server time for file timestamp",
        ["Add.CrcOnly"] = "Extract CRC64 only",
        ["Add.SkipCrc"] = "Skip CRC64 extraction",
        ["Add.Cancel"] = "Cancel",
        ["Add.Enqueue"] = "Add to Queue",
        ["Add.BatchFileName"] = "(Batch task - auto generated)",
        ["Validation.EnterUrl"] = "Enter a URL.",
        ["Validation.SelectDirectory"] = "Choose a save directory.",
        ["Validation.DirectoryMissing"] = "Save directory does not exist.",
        ["Validation.SelectDirectoryFailed"] = "Failed to choose directory: {0}",
        ["Validation.Threads"] = "Threads must be an integer greater than 0.",
        ["Validation.BlockSize"] = "Block size must be a number greater than 0.",
        ["Validation.CrcConflict"] = "CRC64-only mode cannot be used together with skipping CRC64 extraction.",
        ["Settings.Title"] = "Settings",
        ["Settings.Description"] = "Defaults are used for new tasks; site credentials are sent as Basic Auth when a URL matches.",
        ["Settings.Appearance"] = "Appearance",
        ["Settings.Language"] = "Language",
        ["Settings.LanguageHint"] = "Auto chooses Simplified Chinese, Traditional Chinese, or English from the system language.",
        ["Settings.Theme"] = "Theme",
        ["Settings.ThemeHint"] = "System follows the operating system light or dark mode.",
        ["Settings.Auto"] = "Auto",
        ["Settings.LanguageEn"] = "English",
        ["Settings.LanguageZhHans"] = "简体中文",
        ["Settings.LanguageZhHant"] = "繁體中文",
        ["Settings.ThemeSystem"] = "System",
        ["Settings.ThemeLight"] = "Light",
        ["Settings.ThemeDark"] = "Dark",
        ["Settings.Download"] = "Download",
        ["Settings.DownloadDefaults"] = "Download Defaults",
        ["Settings.DefaultThreads"] = "Default Threads",
        ["Settings.DefaultBlock"] = "Default Block MB",
        ["Settings.DefaultConcurrency"] = "Default Concurrency",
        ["Settings.DefaultBehavior"] = "Default Task Behavior",
        ["Settings.DefaultUpdateTimestamp"] = "Use server time by default",
        ["Settings.DefaultSkipCrc"] = "Skip CRC64 extraction by default",
        ["Settings.Network"] = "Network",
        ["Settings.Proxy"] = "HTTP Proxy",
        ["Settings.ProxySystem"] = "Use System Proxy",
        ["Settings.ProxyDisabled"] = "No Proxy",
        ["Settings.ProxyManual"] = "Manual",
        ["Settings.ProxyHint"] = "For manual mode, enter a full proxy URL, for example http://127.0.0.1:7890.",
        ["Settings.SaveAndHeaders"] = "Save & Header",
        ["Settings.DefaultSaveDirectory"] = "Default Save Directory",
        ["Settings.DefaultSaveDirectoryHint"] = "Last-used mode remembers the directory from each successfully added task. Fixed mode defaults to the system Downloads folder.",
        ["Settings.SaveDirectoryLastUsed"] = "Use last download directory",
        ["Settings.SaveDirectoryFixed"] = "Use fixed directory",
        ["Settings.DefaultHeaders"] = "Default HTTP Headers",
        ["Settings.Credentials"] = "Credentials",
        ["Settings.CredentialsTitle"] = "Site Username / Password",
        ["Settings.Add"] = "Add",
        ["Settings.Delete"] = "Delete",
        ["Settings.CredentialsHint"] = "Match can be example.com, *.example.com, or https://example.com/path. Existing URL credentials or Authorization headers are not overwritten.",
        ["Settings.Enabled"] = "Enabled",
        ["Settings.Match"] = "Match",
        ["Settings.Username"] = "Username",
        ["Settings.Password"] = "Password",
        ["Settings.DataFiles"] = "Data Files",
        ["Settings.LocalDataFiles"] = "Local Data Files",
        ["Settings.DataFilesHint"] = "Use these paths for troubleshooting, backup, or migration. The app maintains the files automatically.",
        ["Settings.TasksJson"] = "Task List JSON",
        ["Settings.SettingsJson"] = "Settings JSON",
        ["Settings.Save"] = "Save",
        ["Settings.Cancel"] = "Cancel",
        ["Validation.DefaultThreads"] = "Default threads must be greater than 0.",
        ["Validation.DefaultBlock"] = "Default block size must be greater than 0.",
        ["Validation.DefaultConcurrency"] = "Default concurrency must be 1, 2, 3, 5, or 8.",
        ["Validation.DefaultDirectoryMissing"] = "Default save directory does not exist.",
        ["Validation.ProxyAddress"] = "Manual proxy must be a full URL, for example http://127.0.0.1:7890.",
        ["Validation.CredentialPattern"] = "Credential match pattern cannot be empty.",
        ["Dialog.SelectSaveDirectory"] = "Choose Save Directory",
        ["Dialog.SelectDefaultSaveDirectory"] = "Choose Default Save Directory",
        ["TaskLog.Added"] = "Added to task list",
        ["TaskLog.Queued"] = "Added to queue",
        ["TaskLog.Started"] = "Download started",
        ["TaskLog.Stopping"] = "Stopping; waiting for a safe disk write point...",
        ["TaskLog.Removed"] = "Removed from queue",
        ["Download.SkippedExisting"] = "Target file already exists; skipped",
        ["Download.CrcDone"] = "CRC64 extraction completed",
        ["Download.RangeFallback"] = "Server does not support Range. Switched to single-thread sequential download; reliable resume is unavailable in this mode.",
        ["Download.CanceledWithResume"] = "Download stopped. Safe resumable temporary data has been kept.",
        ["Download.Canceled"] = "Download stopped",
        ["Download.CompletedAverageSpeed"] = "Download completed. Average speed: {0}/s",
        ["Download.SingleThreadCompleted"] = "Single-thread download completed",
        ["Error.TasksJsonLoadFailed"] = "Failed to read tasks.json",
        ["Error.SettingsJsonLoadFailed"] = "Failed to read settings.json; defaults are used"
    };

    private static readonly IReadOnlyDictionary<string, string> ZhHant = new Dictionary<string, string>
    {
        ["App.Name"] = "RobustDownloader",
        ["Main.NewTask"] = "新增任務",
        ["Main.Start"] = "開始",
        ["Main.Stop"] = "停止",
        ["Main.Delete"] = "刪除",
        ["Main.GlobalSpeed"] = "全域速度",
        ["Main.Concurrency"] = "並行",
        ["Main.StartAll"] = "全部開始",
        ["Main.StopAll"] = "全部停止",
        ["Main.Settings"] = "設定",
        ["Main.Queue"] = "下載佇列",
        ["Main.Recent50"] = "最近50筆",
        ["Main.Recent100"] = "最近100筆",
        ["Main.Recent200"] = "最近200筆",
        ["Main.AllTasks"] = "全部任務",
        ["Main.Details"] = "任務詳情",
        ["Main.ShowDetails"] = "顯示詳情",
        ["Main.HideDetails"] = "隱藏詳情",
        ["Main.CollapseDetails"] = "收起",
        ["Main.EmptyTitle"] = "還沒有下載任務",
        ["Main.EmptyBody"] = "新增一個 URL 或批次貼上多行連結後，佇列會顯示下載進度、速度和狀態。",
        ["Main.SelectTaskTitle"] = "選擇一個任務",
        ["Main.SelectTaskBody"] = "右側會顯示儲存位置、日誌、Header 和診斷資訊。",
        ["Main.CopyUrl"] = "複製連結",
        ["Main.CopyFileName"] = "複製檔名",
        ["Main.CopySavePath"] = "複製儲存路徑",
        ["Main.CopyPath"] = "複製路徑",
        ["Main.OpenFolder"] = "開啟資料夾",
        ["Main.Overview"] = "概覽",
        ["Main.Log"] = "日誌",
        ["Main.Headers"] = "Header",
        ["Main.RecentLog"] = "最近日誌",
        ["Main.Diagnostics"] = "診斷",
        ["Diagnostic.None"] = "無異常",
        ["Diagnostic.Initializing"] = "初始化中",
        ["Diagnostic.NoRangeSupport"] = "伺服器不支援 Range",
        ["Diagnostic.DiskBottleneck"] = "磁碟寫入可能受限",
        ["Diagnostic.BufferDeadlock"] = "緩衝區阻塞，正在嘗試恢復",
        ["Diagnostic.NetworkHang"] = "網路連線可能卡住",
        ["Diagnostic.Idle"] = "內部佇列閒置",
        ["Diagnostic.Completed"] = "已完成",
        ["Diagnostic.Error"] = "錯誤",
        ["Main.SettingsSaved"] = "設定已儲存",
        ["Main.OpenedFolder"] = "已開啟儲存位置",
        ["Main.OpenFolderFailed"] = "開啟資料夾失敗: {0}",
        ["Main.Copied"] = "已複製到剪貼簿",
        ["Queue.Empty"] = "佇列閒置 | 無任務",
        ["Queue.Ready"] = "就緒",
        ["Queue.Idle"] = "佇列閒置 | 排隊: {0} | 總任務: {1}",
        ["Queue.Running"] = "下載中: {0} | 並行上限: {1} | 排隊: {2} | 總任務: {3}",
        ["Queue.RecentInfo"] = "顯示最近 {0} / {1} 筆",
        ["Queue.AllInfo"] = "顯示全部 {0} 筆任務",
        ["Column.FileName"] = "檔名",
        ["Column.Status"] = "狀態",
        ["Column.Progress"] = "進度",
        ["Column.Speed"] = "速度",
        ["Column.Eta"] = "剩餘",
        ["Eta.Completed"] = "完成",
        ["Eta.Exists"] = "已存在",
        ["Column.Size"] = "大小",
        ["Column.Mode"] = "模式",
        ["Field.SavePath"] = "儲存路徑",
        ["Field.Url"] = "URL",
        ["Field.Threads"] = "執行緒",
        ["Field.BlockSizeMb"] = "分塊 MB",
        ["Field.Mode"] = "執行模式",
        ["Field.FileTime"] = "檔案時間",
        ["Task.Status.Pending"] = "排隊",
        ["Task.Status.Running"] = "下載中",
        ["Task.Status.Paused"] = "已暫停",
        ["Task.Status.Completed"] = "完成",
        ["Task.Status.Error"] = "錯誤",
        ["Task.Status.Stopped"] = "已停止",
        ["Task.FileTime.Server"] = "使用伺服器時間",
        ["Task.FileTime.Local"] = "保留本機完成時間",
        ["Add.Title"] = "新增下載任務",
        ["Add.Description"] = "貼上一個或多個 URL，確認儲存目錄後加入佇列。",
        ["Add.Urls"] = "下載地址",
        ["Add.UrlsHint"] = "一行一個 URL。單一 URL 可手動覆寫檔名；批次任務會自動識別檔名。",
        ["Add.SaveDirectory"] = "儲存目錄",
        ["Add.Browse"] = "選擇",
        ["Add.FileName"] = "檔名",
        ["Add.FileNameHint"] = "僅在輸入單一 URL 時可編輯。",
        ["Add.Advanced"] = "進階選項",
        ["Add.CustomHeaders"] = "自訂 HTTP Header",
        ["Add.UpdateTimestamp"] = "使用伺服器時間修改檔案時間",
        ["Add.CrcOnly"] = "僅提取 CRC64",
        ["Add.SkipCrc"] = "跳過 CRC64 提取",
        ["Add.Cancel"] = "取消",
        ["Add.Enqueue"] = "加入佇列",
        ["Add.BatchFileName"] = "(批次任務 - 自動產生)",
        ["Validation.EnterUrl"] = "請輸入 URL。",
        ["Validation.SelectDirectory"] = "請選擇儲存目錄。",
        ["Validation.DirectoryMissing"] = "儲存目錄不存在。",
        ["Validation.SelectDirectoryFailed"] = "選擇目錄失敗：{0}",
        ["Validation.Threads"] = "執行緒數必須是大於 0 的整數。",
        ["Validation.BlockSize"] = "分塊大小必須是大於 0 的數字。",
        ["Validation.CrcConflict"] = "僅提取 CRC64 不能與跳過 CRC64 同時啟用。",
        ["Settings.Title"] = "全域設定",
        ["Settings.Description"] = "預設值會用於新增任務；站台憑證會在符合 URL 時自動攜帶 Basic Auth。",
        ["Settings.Appearance"] = "外觀",
        ["Settings.Language"] = "語言",
        ["Settings.LanguageHint"] = "自動會根據系統語言選擇簡體中文、繁體中文或英文。",
        ["Settings.Theme"] = "主題",
        ["Settings.ThemeHint"] = "跟隨系統會使用作業系統目前的淺色或深色模式。",
        ["Settings.Auto"] = "自動",
        ["Settings.LanguageEn"] = "English",
        ["Settings.LanguageZhHans"] = "简体中文",
        ["Settings.LanguageZhHant"] = "繁體中文",
        ["Settings.ThemeSystem"] = "跟隨系統",
        ["Settings.ThemeLight"] = "淺色",
        ["Settings.ThemeDark"] = "深色",
        ["Settings.Download"] = "下載參數",
        ["Settings.DownloadDefaults"] = "預設下載參數",
        ["Settings.DefaultThreads"] = "預設執行緒",
        ["Settings.DefaultBlock"] = "預設分塊 MB",
        ["Settings.DefaultConcurrency"] = "預設並行",
        ["Settings.DefaultBehavior"] = "預設任務行為",
        ["Settings.DefaultUpdateTimestamp"] = "預設使用伺服器時間修改檔案時間",
        ["Settings.DefaultSkipCrc"] = "預設跳過 CRC64 提取",
        ["Settings.Network"] = "網路",
        ["Settings.Proxy"] = "HTTP 代理",
        ["Settings.ProxySystem"] = "跟隨系統",
        ["Settings.ProxyDisabled"] = "不使用代理",
        ["Settings.ProxyManual"] = "手動填寫",
        ["Settings.ProxyHint"] = "手動模式請填寫完整代理地址，例如 http://127.0.0.1:7890。",
        ["Settings.SaveAndHeaders"] = "儲存與 Header",
        ["Settings.DefaultSaveDirectory"] = "預設儲存目錄",
        ["Settings.DefaultSaveDirectoryHint"] = "使用上一次目錄時，每次成功加入任務後會記住當次儲存目錄；固定目錄預設使用系統下載目錄。",
        ["Settings.SaveDirectoryLastUsed"] = "使用上一次的下載目錄",
        ["Settings.SaveDirectoryFixed"] = "使用固定目錄",
        ["Settings.DefaultHeaders"] = "預設 HTTP Header",
        ["Settings.Credentials"] = "站台憑證",
        ["Settings.CredentialsTitle"] = "站台使用者名稱/密碼",
        ["Settings.Add"] = "新增",
        ["Settings.Delete"] = "刪除",
        ["Settings.CredentialsHint"] = "匹配可以填寫 example.com、*.example.com 或 https://example.com/path。URL 內已有使用者名稱密碼或 Header 已含 Authorization 時不會覆蓋。",
        ["Settings.Enabled"] = "啟用",
        ["Settings.Match"] = "匹配",
        ["Settings.Username"] = "使用者名稱",
        ["Settings.Password"] = "密碼",
        ["Settings.DataFiles"] = "資料檔案",
        ["Settings.LocalDataFiles"] = "本機資料檔案",
        ["Settings.DataFilesHint"] = "這些路徑用於排查、備份或遷移設定。檔案內容仍由應用程式自動維護。",
        ["Settings.TasksJson"] = "任務列表 JSON",
        ["Settings.SettingsJson"] = "設定項 JSON",
        ["Settings.Save"] = "儲存",
        ["Settings.Cancel"] = "取消",
        ["Validation.DefaultThreads"] = "預設執行緒必須大於 0。",
        ["Validation.DefaultBlock"] = "預設分塊必須大於 0。",
        ["Validation.DefaultConcurrency"] = "預設並行必須是 1、2、3、5、8。",
        ["Validation.DefaultDirectoryMissing"] = "預設儲存目錄不存在。",
        ["Validation.ProxyAddress"] = "手動代理地址必須是完整地址，例如 http://127.0.0.1:7890。",
        ["Validation.CredentialPattern"] = "站台憑證的匹配規則不能為空。",
        ["Dialog.SelectSaveDirectory"] = "選擇儲存目錄",
        ["Dialog.SelectDefaultSaveDirectory"] = "選擇預設儲存目錄",
        ["TaskLog.Added"] = "已加入任務列表",
        ["TaskLog.Queued"] = "已加入佇列",
        ["TaskLog.Started"] = "開始下載",
        ["TaskLog.Stopping"] = "正在停止，等待安全寫盤點...",
        ["TaskLog.Removed"] = "已從佇列移除",
        ["Download.SkippedExisting"] = "目標檔案已存在，已跳過",
        ["Download.CrcDone"] = "CRC64 提取完成",
        ["Download.RangeFallback"] = "伺服器不支援 Range，已切換為單執行緒順序下載；該模式無法可靠斷點續傳。",
        ["Download.CanceledWithResume"] = "使用者已停止下載。已保留可安全續傳的暫存資料。",
        ["Download.Canceled"] = "使用者已停止下載",
        ["Download.CompletedAverageSpeed"] = "下載完成，平均速度 {0}/s",
        ["Download.SingleThreadCompleted"] = "單執行緒下載完成",
        ["Error.TasksJsonLoadFailed"] = "tasks.json 讀取失敗",
        ["Error.SettingsJsonLoadFailed"] = "settings.json 讀取失敗，已使用預設設定"
    };
}
