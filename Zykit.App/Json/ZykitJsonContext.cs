using System.Collections.Generic;
using System.Text.Json.Serialization;
using Zykit.App.Models;

namespace Zykit.App.Json;

/// <summary>
/// Native AOT 兼容的 JSON 序列化上下文 (源生成)。
/// 用于本地配置文件的序列化/反序列化，避免反射。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AuthInfo))]
[JsonSerializable(typeof(List<LocalCacheEntry>))]
[JsonSerializable(typeof(List<SigningPair>))]
[JsonSerializable(typeof(AppVersionInfo))]
[JsonSerializable(typeof(UpdateInfo))]
[JsonSerializable(typeof(ThemeSettings))]
[JsonSerializable(typeof(AppSettings))]
internal partial class ZykitJsonContext : JsonSerializerContext
{
}