using Secs4Net;
using Secs4Net.Sml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SecsUtil;

public class SecsItemData
{
    public string Type { get; set; } = string.Empty;
    public List<object> Values { get; set; } = new List<object>();
    public List<SecsItemData>? Children { get; set; }

    public Dictionary<string, object>? Data { get; set; }
}

public class SecsItemDataYamlConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(SecsItemData);
    }

    public object ReadYaml(IParser parser, Type type)
    {
        try
        {
            var data = new SecsItemData();
            
            if (parser.TryConsume<MappingStart>(out _))
            {
                ParseMapping(parser, data);
            }
            else if (parser.TryConsume<Scalar>(out var scalar))
            {
                data.Type = scalar.Value;
                ParseValueOrChildren(parser, data);
            }

            return data;
        }
        catch
        {
            return new SecsItemData();
        }
    }
    
    private void ParseMapping(IParser parser, SecsItemData data)
    {
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            if (!parser.TryConsume<Scalar>(out var keyScalar))
            {
                SkipToNextMappingEnd(parser);
                break;
            }
            
            var key = keyScalar.Value;
            data.Type = key;
            
            ParseValueOrChildren(parser, data);
        }
    }
    
    private void SkipToNextMappingEnd(IParser parser)
    {
        int depth = 1;
        while (depth > 0 && parser.MoveNext())
        {
            if (parser.Current is MappingStart) depth++;
            else if (parser.Current is MappingEnd) depth--;
            else if (parser.Current is SequenceStart) depth++;
            else if (parser.Current is SequenceEnd) depth--;
        }
    }
    
    private void ParseValueOrChildren(IParser parser, SecsItemData data)
    {
        try
        {
            if (parser.TryConsume<SequenceStart>(out _))
            {
                ParseSequence(parser, data);
            }
            else if (parser.TryConsume<MappingStart>(out _))
            {
                data.Children = new List<SecsItemData>();
                ParseChildrenMapping(parser, data.Children);
            }
            else if (parser.TryConsume<Scalar>(out var scalar))
            {
                data.Values = new List<object>();
                data.Values.Add(ParseValue(scalar.Value));
            }
        }
        catch
        {
        }
    }
    
    private void ParseSequence(IParser parser, SecsItemData data)
    {
        if (parser.TryConsume<SequenceEnd>(out _))
            return;
            
        var firstEvent = parser.Current;
        if (firstEvent is MappingStart)
        {
            data.Children = new List<SecsItemData>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                try
                {
                    var childData = (SecsItemData)ReadYaml(parser, typeof(SecsItemData));
                    if (!string.IsNullOrEmpty(childData.Type))
                    {
                        data.Children.Add(childData);
                    }
                }
                catch
                {
                    SkipToNextSequenceEnd(parser);
                    break;
                }
            }
        }
        else if (firstEvent is Scalar)
        {
            data.Values = new List<object>();
            while (!parser.TryConsume<SequenceEnd>(out _))
            {
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    data.Values.Add(ParseValue(scalar.Value));
                }
                else
                {
                    SkipToNextSequenceEnd(parser);
                    break;
                }
            }
        }
        else
        {
            SkipToNextSequenceEnd(parser);
        }
    }
    
    private void SkipToNextSequenceEnd(IParser parser)
    {
        int depth = 1;
        while (depth > 0 && parser.MoveNext())
        {
            if (parser.Current is SequenceStart) depth++;
            else if (parser.Current is SequenceEnd) depth--;
            else if (parser.Current is MappingStart) depth++;
            else if (parser.Current is MappingEnd) depth--;
        }
    }
    
    private void ParseChildrenMapping(IParser parser, List<SecsItemData> children)
    {
        while (!parser.TryConsume<MappingEnd>(out _))
        {
            if (!parser.TryConsume<Scalar>(out var childKeyScalar))
            {
                SkipToNextMappingEnd(parser);
                break;
            }
            
            var childKey = childKeyScalar.Value;
            var childData = new SecsItemData { Type = childKey };
            ParseValueOrChildren(parser, childData);
            children.Add(childData);
        }
    }

    
    private object ParseValue(string value)
    {
        if (value.StartsWith("\"") && value.EndsWith("\""))
            return value.Substring(1, value.Length - 2);

        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        if (byte.TryParse(value, out var byteValue))
            return byteValue;

        if (ushort.TryParse(value, out var ushortValue))
            return ushortValue;

        if (uint.TryParse(value, out var uintValue))
            return uintValue;

        if (int.TryParse(value, out var intValue))
            return intValue;

        if (float.TryParse(value, out var floatValue))
            return floatValue;

        if (double.TryParse(value, out var doubleValue))
            return doubleValue;

        return value;
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type)
    {
        if (value is not SecsItemData data)
            return;

        emitter.Emit(new MappingStart());
        WriteYamlMapping(emitter, data);
        emitter.Emit(new MappingEnd());
    }
    
    private void WriteYamlMapping(IEmitter emitter, SecsItemData data)
    {
        emitter.Emit(new Scalar(data.Type));

        if (data.Children != null && data.Children.Any())
        {
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

            foreach (var child in data.Children)
            {
                emitter.Emit(new MappingStart());
                WriteYamlMapping(emitter, child);
                emitter.Emit(new MappingEnd());
            }

            emitter.Emit(new SequenceEnd());
        }
        else
        {
            emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

            if (data.Values != null)
            {
                foreach (var val in data.Values)
                {
                    if (val is string str)
                        emitter.Emit(new Scalar(str ?? string.Empty));
                    else
                        emitter.Emit(new Scalar(val?.ToString() ?? string.Empty));
                }
            }

            emitter.Emit(new SequenceEnd());
        }
    }
}

public class MessageTemplate
{
    [YamlDotNet.Serialization.YamlIgnore]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public byte Stream { get; set; }
    public byte Function { get; set; }
    public bool ReplyExpected { get; set; } = true;
    public bool AutoReply { get; set; }
    public SecsItemData? SecsItem { get; set; }
    
    public SecsMessage CreateMessage()
    {
        var msg = new SecsMessage(Stream, Function, ReplyExpected)
        {
            Name = Name
        };
        
        if (SecsItem != null)
        {
            try
            {
                msg.SecsItem = SecsItem.ToSecsItem();
            }
            catch
            {
                msg.SecsItem = null;
            }
        }
        
        return msg;
    }
    
    [YamlDotNet.Serialization.YamlIgnore]
    public string DisplayText => $"S{Stream}F{Function} - {Name}";

    [YamlDotNet.Serialization.YamlIgnore]
    public string TemplateKey => $"{Stream}:{Function}:{Name}";

    public bool Matches(MessageTemplate other) =>
        Stream == other.Stream && Function == other.Function && Name == other.Name;
}

public static class SecsItemDataExtensions
{
    public static Item ToSecsItem(this SecsItemData data)
    {
        return data.Type switch
        {
            "List" => Item.L(data.Children?.Select(c => c.ToSecsItem()).ToArray() ?? Array.Empty<Item>()),
            "ASCII" => Item.A(data.Values.Select(v => v.ToString()).FirstOrDefault() ?? string.Empty),
            "Binary" => Item.B(data.Values.Select(v => Convert.ToByte(v)).ToArray()),
            "U1" => Item.U1(data.Values.Select(v => Convert.ToByte(v)).ToArray()),
            "U2" => Item.U2(data.Values.Select(v => Convert.ToUInt16(v)).ToArray()),
            "U4" => Item.U4(data.Values.Select(v => Convert.ToUInt32(v)).ToArray()),
            "I1" => Item.I1(data.Values.Select(v => Convert.ToSByte(v)).ToArray()),
            "I2" => Item.I2(data.Values.Select(v => Convert.ToInt16(v)).ToArray()),
            "I4" => Item.I4(data.Values.Select(v => Convert.ToInt32(v)).ToArray()),
            "F4" => Item.F4(data.Values.Select(v => Convert.ToSingle(v)).ToArray()),
            "F8" => Item.F8(data.Values.Select(v => Convert.ToDouble(v)).ToArray()),
            "Boolean" => Item.Boolean(data.Values.Select(v => Convert.ToBoolean(v)).ToArray()),
            _ => throw new NotSupportedException($"Unsupported SecsItem type: {data.Type}")
        };
    }
    
    public static SecsItemData? FromSecsItem(Item? item)
    {
        if (item == null) return null;
        
        switch (item.Format)
        {
            case SecsFormat.List:
                return new SecsItemData
                {
                    Type = "List",
                    Children = item.Items.Select(FromSecsItem).Where(c => c != null).ToList()!
                };
            case SecsFormat.ASCII:
                return new SecsItemData
                {
                    Type = "ASCII",
                    Values = new List<object> { item.GetString() ?? string.Empty }
                };
            case SecsFormat.Binary:
                return new SecsItemData
                {
                    Type = "Binary",
                    Values = item.GetMemory<byte>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.U1:
                return new SecsItemData
                {
                    Type = "U1",
                    Values = item.GetMemory<byte>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.U2:
                return new SecsItemData
                {
                    Type = "U2",
                    Values = item.GetMemory<ushort>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.U4:
                return new SecsItemData
                {
                    Type = "U4",
                    Values = item.GetMemory<uint>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.I1:
                return new SecsItemData
                {
                    Type = "I1",
                    Values = item.GetMemory<sbyte>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.I2:
                return new SecsItemData
                {
                    Type = "I2",
                    Values = item.GetMemory<short>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.I4:
                return new SecsItemData
                {
                    Type = "I4",
                    Values = item.GetMemory<int>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.F4:
                return new SecsItemData
                {
                    Type = "F4",
                    Values = item.GetMemory<float>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.F8:
                return new SecsItemData
                {
                    Type = "F8",
                    Values = item.GetMemory<double>().Span.ToArray().Cast<object>().ToList()
                };
            case SecsFormat.Boolean:
                return new SecsItemData
                {
                    Type = "Boolean",
                    Values = item.GetMemory<bool>().Span.ToArray().Cast<object>().ToList()
                };
            default:
                return null;
        }
    }
    
    public static string? ToSmlString(this SecsItemData? data)
    {
        if (data == null) return null;
        try
        {
            var item = data.ToSecsItem();
            return item.GetSml();
        }
        catch
        {
            return null;
        }
    }
    
    public static SecsItemData? FromSmlString(string? sml)
    {
        if (string.IsNullOrWhiteSpace(sml)) return null;
        try
        {
            var item = sml.ToSecsMessage()?.SecsItem;
            return FromSecsItem(item);
        }
        catch
        {
            return null;
        }
    }
    
    public static string ToCompactYaml(this SecsItemData data)
    {
        return SerializeToCompactYaml(data);
    }

    private static string SerializeToCompactYaml(SecsItemData data, int indent = 2)
    {
        var spaces = new string(' ', indent);
        
        if (data.Type == "List" && data.Children != null && data.Children.Any())
        {
            var childrenYaml = string.Join("\n", data.Children.Select(c => $"{spaces}{SerializeToCompactYaml(c, indent + 2)}"));
            return $"List:\n{childrenYaml}";
        }
        else if (!string.IsNullOrEmpty(data.Type) && data.Values != null && data.Values.Any())
        {
            var valuesStr = data.Type == "ASCII" 
                ? $"[\"{string.Join("\", \"", data.Values)}\"]" 
                : $"[{string.Join(", ", data.Values)}]";
            return $"{data.Type}: {valuesStr}";
        }
        else if (data.Type == "List")
        {
            return "List: []";
        }
        return $"{data.Type}: []";
    }

    public static SecsItemData? FromCompactYaml(string yaml)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(NullNamingConvention.Instance)
                .Build();
            
            var data = deserializer.Deserialize<Dictionary<string, object>>(yaml);
            return ParseCompactYaml(data);
        }
        catch
        {
            return null;
        }
    }

    private static SecsItemData? ParseCompactYaml(Dictionary<string, object> data)
    {
        if (data.Count == 0) return null;

        var type = data.Keys.First();
        var value = data[type];

        var secsItem = new SecsItemData { Type = type };

        if (value is List<object> values)
        {
            secsItem.Values = values;
        }
        else if (value is Dictionary<string, object> dict)
        {
            secsItem.Type = type;
            secsItem.Children = new List<SecsItemData>();
            
            foreach (var (childType, childValue) in dict)
            {
                var childDict = new Dictionary<string, object> { { childType, childValue } };
                var child = ParseCompactYaml(childDict);
                if (child != null)
                {
                    secsItem.Children.Add(child);
                }
            }
        }

        return secsItem;
    }

    public static string ToYamlString(this SecsItemData? data)
    {
        if (data == null) return string.Empty;
        return BuildYamlString(data, 0);
    }
    
    private static string BuildYamlString(SecsItemData data, int indent)
    {
        var spaces = new string(' ', indent);
        
        if (data.Values != null && data.Values.Count > 0)
        {
            var valuesStr = string.Join(", ", data.Values);
            return $"{spaces}{data.Type}: [{valuesStr}]";
        }
        else if (data.Children != null && data.Children.Any())
        {
            var childrenStr = string.Join("\n", data.Children.Select(c => BuildYamlString(c, indent + 2)));
            return $"{spaces}{data.Type}:\n{childrenStr}";
        }
        else
        {
            return $"{spaces}{data.Type}: []";
        }
    }

    public static string ToCompactString(this SecsItemData? data)
    {
        if (data == null) return string.Empty;
        return BuildCompactString(data, 0);
    }

    private static string BuildCompactString(SecsItemData data, int indent)
    {
        var spaces = new string(' ', indent);
        
        if (data.Type == "List" && data.Children != null && data.Children.Any())
        {
            var childrenStr = string.Join("\n", data.Children.Select(c => BuildCompactString(c, indent + 2)));
            return $"{spaces}{data.Type} [\n{childrenStr}\n{spaces}]";
        }
        else if (!string.IsNullOrEmpty(data.Type) && data.Values != null && data.Values.Any())
        {
            var valuesStr = data.Type == "ASCII" 
                ? $"\"{string.Join("\", \"", data.Values)}\"" 
                : string.Join(", ", data.Values);
            return $"{spaces}{data.Type}: [{valuesStr}]";
        }
        else if (data.Type == "List")
        {
            return $"{spaces}{data.Type}: []";
        }
        return $"{spaces}{data.Type}: []";
    }

    }

public class TemplateManager
{
    private static readonly string _templatesDir = Path.Combine(AppContext.BaseDirectory, "Templates");
    private static readonly string _defaultTemplateFile = Path.Combine(_templatesDir, "default.yaml");
    
    private static readonly IDeserializer _yamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .WithTypeConverter(new SecsItemDataYamlConverter())
        .Build();
    
    private static readonly ISerializer _yamlSerializer = new SerializerBuilder()
        .WithNamingConvention(NullNamingConvention.Instance)
        .WithIndentedSequences()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithTypeConverter(new SecsItemDataYamlConverter())
        .Build();

    private static int FindTemplateIndex(List<MessageTemplate> templates, MessageTemplate template)
    {
        var index = templates.FindIndex(t => t.Id == template.Id);
        if (index >= 0)
            return index;

        return templates.FindIndex(t => t.Matches(template));
    }

    private static void EnsureTemplateIds(List<MessageTemplate> templates)
    {
        foreach (var template in templates)
        {
            if (string.IsNullOrEmpty(template.Id))
                template.Id = Guid.NewGuid().ToString();
        }
    }

    public static List<MessageTemplate> LoadAllTemplates()
    {
        EnsureDefaultTemplateFile();
        return LoadTemplatesFromFile(_defaultTemplateFile);
    }

    private static void EnsureDefaultTemplateFile()
    {
        Directory.CreateDirectory(_templatesDir);
        
        if (!File.Exists(_defaultTemplateFile))
        {
            var defaultTemplates = GetDefaultTemplates();
            SaveTemplatesToFile(defaultTemplates, _defaultTemplateFile);
        }
    }

    private static List<MessageTemplate> GetDefaultTemplates()
    {
        return new List<MessageTemplate>
        {
            new MessageTemplate
            {
                Name = "S1F1 - Are You There",
                Description = "询问设备是否在线",
                Stream = 1,
                Function = 1,
                ReplyExpected = true
            },
            new MessageTemplate
            {
                Name = "S1F2 - On Line Data",
                Description = "响应在线数据",
                Stream = 1,
                Function = 2,
                ReplyExpected = false,
                AutoReply = true,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U2", Values = new List<object> { 0 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "ONLINE" } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S1F3 - Selected Equipment Status Request",
                Description = "选定设备状态请求",
                Stream = 1,
                Function = 3,
                ReplyExpected = true
            },
            new MessageTemplate
            {
                Name = "S1F4 - Selected Equipment Status Data",
                Description = "选定设备状态数据",
                Stream = 1,
                Function = 4,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U2", Values = new List<object> { 0 } },
                        new SecsItemData { Type = "U2", Values = new List<object> { 0 } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S1F13 - Establish Communications Request",
                Description = "建立通信请求",
                Stream = 1,
                Function = 13,
                ReplyExpected = true,
                SecsItem = new SecsItemData { Type = "ASCII", Values = new List<object> { "Hello SecsUtil" } }
            },
            new MessageTemplate
            {
                Name = "S1F14 - Establish Communications Response",
                Description = "建立通信响应",
                Stream = 1,
                Function = 14,
                ReplyExpected = false,
                AutoReply = true,
                SecsItem = new SecsItemData { Type = "ASCII", Values = new List<object> { "Communication OK" } }
            },
            new MessageTemplate
            {
                Name = "S1F65 - CR (Communication Request)",
                Description = "通信请求",
                Stream = 1,
                Function = 65,
                ReplyExpected = true,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "MDLN" } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "Softrev" } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S1F66 - CRA (Communication Request Acknowledge)",
                Description = "通信请求确认",
                Stream = 1,
                Function = 66,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "Binary", Values = new List<object> { 0 } },
                        new SecsItemData
                        {
                            Type = "List",
                            Children = new List<SecsItemData>
                            {
                                new SecsItemData { Type = "ASCII", Values = new List<object> { "SECSUTIL" } },
                                new SecsItemData { Type = "ASCII", Values = new List<object> { "1.0.0" } }
                            }
                        }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S2F17 - Date & Time Request",
                Description = "日期时间请求",
                Stream = 2,
                Function = 17,
                ReplyExpected = true
            },
            new MessageTemplate
            {
                Name = "S2F18 - Date & Time Data",
                Description = "日期时间数据",
                Stream = 2,
                Function = 18,
                ReplyExpected = false,
                SecsItem = new SecsItemData { Type = "ASCII", Values = new List<object> { "2024-01-15 10:30:00" } }
            },
            new MessageTemplate
            {
                Name = "S5F1 - Alarm Report Send",
                Description = "报警报告发送",
                Stream = 5,
                Function = 1,
                ReplyExpected = true,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "ALM001" } },
                        new SecsItemData { Type = "U1", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "Boolean", Values = new List<object> { true } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S5F2 - Alarm Report Acknowledge",
                Description = "报警报告确认",
                Stream = 5,
                Function = 2,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "U1", Values = new List<object> { 0 } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S6F11 - Event Report",
                Description = "事件报告发送",
                Stream = 6,
                Function = 11,
                ReplyExpected = true,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData
                        {
                            Type = "List",
                            Children = new List<SecsItemData>
                            {
                                new SecsItemData { Type = "U4", Values = new List<object> { 1001 } },
                                new SecsItemData
                                {
                                    Type = "List",
                                    Children = new List<SecsItemData>
                                    {
                                        new SecsItemData { Type = "ASCII", Values = new List<object> { "TEST" } },
                                        new SecsItemData { Type = "U1", Values = new List<object> { 1 } }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S6F12 - Event Report Acknowledge",
                Description = "事件报告确认",
                Stream = 6,
                Function = 12,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "U1", Values = new List<object> { 0 } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S7F5 - Process Program Load Inquiry",
                Description = "过程程序加载查询",
                Stream = 7,
                Function = 5,
                ReplyExpected = true,
                SecsItem = new SecsItemData { Type = "ASCII", Values = new List<object> { "PROG001" } }
            },
            new MessageTemplate
            {
                Name = "S7F6 - Process Program Load Grant",
                Description = "过程程序加载授权",
                Stream = 7,
                Function = 6,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U1", Values = new List<object> { 0 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "PROG001" } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S14F1 - Get Attribute Request",
                Description = "获取属性请求",
                Stream = 14,
                Function = 1,
                ReplyExpected = true,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "EquipmentID" } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S14F2 - Get Attribute Data",
                Description = "获取属性数据",
                Stream = 14,
                Function = 2,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "TOOL001" } }
                    }
                }
            },
            new MessageTemplate
            {
                Name = "S20F1 - Equipment Constant Request",
                Description = "设备常量请求",
                Stream = 20,
                Function = 1,
                ReplyExpected = true,
                SecsItem = new SecsItemData { Type = "U4", Values = new List<object> { 1 } }
            },
            new MessageTemplate
            {
                Name = "S20F2 - Equipment Constant Data",
                Description = "设备常量数据",
                Stream = 20,
                Function = 2,
                ReplyExpected = false,
                SecsItem = new SecsItemData
                {
                    Type = "List",
                    Children = new List<SecsItemData>
                    {
                        new SecsItemData { Type = "U4", Values = new List<object> { 1 } },
                        new SecsItemData { Type = "ASCII", Values = new List<object> { "CONST001" } },
                        new SecsItemData { Type = "U4", Values = new List<object> { 42 } }
                    }
                }
            }
        };
    }

    public static List<MessageTemplate> LoadTemplatesFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            return new List<MessageTemplate>();

        var yaml = File.ReadAllText(filePath);
        var templates = _yamlDeserializer.Deserialize<List<MessageTemplate>>(yaml) ?? new List<MessageTemplate>();
        EnsureTemplateIds(templates);
        return templates;
    }

    private static void SaveTemplatesToFile(List<MessageTemplate> templates, string filePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? string.Empty);
            var yaml = _yamlSerializer.Serialize(templates);
            File.WriteAllText(filePath, yaml);
        }
        catch
        {
        }
    }

    public static void SaveAllTemplates(List<MessageTemplate> templates)
    {
        SaveTemplatesToFile(templates, _defaultTemplateFile);
    }

    public static void AddTemplate(MessageTemplate template)
    {
        var templates = LoadAllTemplates();
        template.Id = Guid.NewGuid().ToString();
        templates.Add(template);
        SaveAllTemplates(templates);
    }

    public static void UpdateTemplate(MessageTemplate template)
    {
        var templates = LoadAllTemplates();
        var index = FindTemplateIndex(templates, template);
        if (index >= 0)
        {
            template.Id = templates[index].Id;
            templates[index] = template;
            SaveAllTemplates(templates);
        }
    }

    public static void DeleteTemplate(MessageTemplate template)
    {
        var templates = LoadAllTemplates();
        templates.RemoveAll(t => t.Id == template.Id || t.Matches(template));
        SaveAllTemplates(templates);
    }

    public static void DeleteTemplate(string templateId)
    {
        var templates = LoadAllTemplates();
        templates.RemoveAll(t => t.Id == templateId);
        SaveAllTemplates(templates);
    }

    public static void ExportTemplates(List<MessageTemplate> templates, string filePath)
    {
        try
        {
            var yaml = _yamlSerializer.Serialize(templates);
            File.WriteAllText(filePath, yaml, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Export templates failed: {ex.Message}");
        }
    }

    public static List<MessageTemplate> ImportTemplates(string filePath, bool overwrite = false)
    {
        if (!File.Exists(filePath))
            return new List<MessageTemplate>();

        var imported = LoadTemplatesFromFile(filePath);
        if (imported.Count == 0)
            return imported;

        foreach (var template in imported)
            template.Id = Guid.NewGuid().ToString();

        if (overwrite)
        {
            SaveAllTemplates(imported);
            return imported;
        }

        var existing = LoadAllTemplates();
        foreach (var template in imported)
        {
            var index = FindTemplateIndex(existing, template);
            if (index >= 0)
            {
                template.Id = existing[index].Id;
                existing[index] = template;
            }
            else
            {
                existing.Add(template);
            }
        }

        SaveAllTemplates(existing);
        return imported;
    }

    public static void ExportAllTemplates(string filePath)
    {
        try
        {
            var allTemplates = LoadAllTemplates();
            if (allTemplates == null || allTemplates.Count == 0)
            {
                Console.WriteLine("No templates to export");
                return;
            }
            var yaml = _yamlSerializer.Serialize(allTemplates);
            File.WriteAllText(filePath, yaml, Encoding.UTF8);
            Console.WriteLine($"Exported {allTemplates.Count} templates to {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Export all templates failed: {ex.Message}");
        }
    }
}
