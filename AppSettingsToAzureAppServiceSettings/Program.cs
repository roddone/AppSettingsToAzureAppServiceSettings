using CommandLine;
using System.Security;
using System.Text.Json;

if (args.Length == 0)
{
    Console.WriteLine("A file path must be specified");
    return;
}

var _serializerOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

Parser.Default.ParseArguments<Arguments>(args).WithParsed(o =>
{
    if (!File.Exists(o.FilePath))
    {
        Console.WriteLine("Input file does not exists");
        return;
    }

    if (o.OutputType == OutputTypeKind.File && string.IsNullOrWhiteSpace(o.OutputFilePath))
    {
        Console.WriteLine("An output file path must be specified");
        return;
    }

    //parse read input file
    JsonDocument obj = JsonDocument.Parse(File.ReadAllText(o.FilePath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

    //initialize output dictionary
    Dictionary<string, string> appSettings = new Dictionary<string, string>();

    //Process recursively
    Recurse(ref appSettings, string.Empty, obj.RootElement.EnumerateObject());


    //next is just writing to the correct output
    string jsonText = o.OutputFormat switch
    {
        //transform into list with Azure AppService properties
        OutputFormatKind.Azure => JsonSerializer.Serialize(appSettings.Select(kvp => new { Name = kvp.Key, kvp.Value, SlotSetting = o.AreSlotSettings }), _serializerOptions),
        OutputFormatKind.DockerCompose => string.Join("\n", appSettings.Select(a => $"- {a.Key}={a.Value}")),
        _ => throw new NotImplementedException()
    };

    if (o.OutputType == OutputTypeKind.Console)
    {
        Console.WriteLine(jsonText);
    }
    else if (o.OutputType == OutputTypeKind.File)
    {
        //file exists and we don't want to overwrite it
        if (File.Exists(o.OutputFilePath) && !o.OverWriteExistingFile)
        {
            Console.WriteLine($"file \"{o.OutputFilePath}\" already exists");
            return;
        }

        //File exists (or does not exists, whatever), we delete and rewrite it
        File.Delete(o.OutputFilePath ?? "");
        File.WriteAllText(o.OutputFilePath ?? "", jsonText);
    }
});

static void Recurse(ref Dictionary<string, string> settings, string key, JsonElement.ObjectEnumerator elements)
{
    foreach (var prop in elements)
    {
        string newKey = string.IsNullOrEmpty(key) ? prop.Name : $"{key}:{prop.Name}";

        //recurse each sub property
        if (prop.Value.ValueKind is JsonValueKind.Object)
        {
            Recurse(ref settings, newKey, prop.Value.EnumerateObject());
        }
        else if (prop.Value.ValueKind is JsonValueKind.Array)
        {
            int idx = 0;

            //process each array item
            foreach (var sub in prop.Value.EnumerateArray())
            {
                string subKey = $"{newKey}:{idx}";

                if (sub.ValueKind == JsonValueKind.Object)
                {
                    //recurse each object in the array
                    Recurse(ref settings, subKey, sub.EnumerateObject());
                }
                //save item
                else settings.Add(subKey, sub.ToString());

                idx++;
            }
        }
        else
        {
            //save item
            settings.Add(newKey, prop.Value.ToString());
        }
    }
}

class Arguments
{
    [Option('i', Required = true, HelpText = "Input file path")]
    public string FilePath { get; set; } = string.Empty;

    [Option('o', Required = false, HelpText = "Output file path")]
    public string? OutputFilePath { get; set; }

    [Option('k', Required = false, Default = OutputTypeKind.Console, HelpText = "Output type (Console or File)")]
    public OutputTypeKind OutputType { get; set; }

    [Option('f', Required = false, HelpText = "Output format", Default = OutputFormatKind.Azure)]
    public OutputFormatKind OutputFormat { get; set; }

    [Option("oef", Required = false, Default = false, HelpText = "Wether to overwrite or not output file if it does already exists")]
    public bool OverWriteExistingFile { get; set; }

    [Option("ss", Required = false, Default = false, HelpText = "Wether to consider the settings as slot settings")]
    public bool AreSlotSettings { get; set; }
}

enum OutputTypeKind
{
    File, Console
}

enum OutputFormatKind
{
    Azure, DockerCompose
}

