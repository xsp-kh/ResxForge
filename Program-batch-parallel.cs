using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Text.RegularExpressions;

class Program
{
    // ======================
    // CONFIG (HARD-CODED)
    // ======================
    private const string ResxFolder = @"C:\Users\xxx\source\repos\ResxForge\Resources";
    private const string ConfigFolder = @"C:\Users\xxx\source\repos\ResxForge\config";
    private const string CacheFolder = @"C:\Users\xxx\source\repos\ResxForge\cache-batch";
    private static string OllamaModel = "translategemma:27b";
    private const string OllamaUrl = "http://127.0.0.1:11434/api/generate";
    private const string Excluded = "";

    private static readonly HashSet<string> ReviewLogExcludedPages =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ""
        };
    private static readonly string ReviewLogPath =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        "review.log"
    );

    private static readonly StringBuilder FinalLog = new();
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    private static Process? OllamaProcess;
    private static bool ForceOverwriteCache = false;

    static Program()
    {
        AppDomain.CurrentDomain.ProcessExit += (s, e) => StopOllama();
        Console.CancelKeyPress += (s, e) =>
        {
            StopOllama();
            e.Cancel = false;
        };
    }

    // ======================
    // LANGUAGES
    // ======================
    private static readonly List<string> Languages = new()
    {
        "km","zh","vi","th","de","ja","fr","id","ms","ko","nl","it","es","hi","ru","pt","cs","lo","sv"
    };

    private static IReadOnlyList<string> TargetLangs => Languages.Where(l => l != "en").ToList();

    private static readonly Dictionary<string, string> LangNames = new()
    {
        ["km"] = "Khmer",
        ["zh"] = "Simplified Chinese",
        ["vi"] = "Vietnamese",
        ["th"] = "Thai",
        ["de"] = "German",
        ["ja"] = "Japanese",
        ["fr"] = "French",
        ["id"] = "Indonesian",
        ["ms"] = "Malay",
        ["ko"] = "Korean",
        ["nl"] = "Dutch",
        ["it"] = "Italian",
        ["es"] = "Spanish",
        ["hi"] = "Hindi",
        ["ru"] = "Russian",
        ["pt"] = "Portuguese",
        ["cs"] = "Czech",
        ["lo"] = "Lao",
        ["sv"] = "Swedish"
    };

    // ======================
    // FIXED TRANSLATIONS FOR SPECIFIC KEYS
    // ======================
    private static readonly Dictionary<string, Dictionary<string, string>> KeyOverrides = new()
    {
        ["km"] = new()
        {
            ["Language"] = "ភាសាអង់គ្លេស"
        },
        ["zh"] = new()
        {
            ["Wer rastet, der rostet."] = "谁停滞不前，谁就会生锈。"
        }
    };

    // ======================
    // GLOSSARY + ECHO WATCHERS
    // ======================

    private static FileSystemWatcher? GlossaryWatcher;
    private static FileSystemWatcher? EchoWatcher;

    // ======================
    // GLOSSARY CONFIG
    // ======================
    private static Dictionary<string, Dictionary<string, string>> Glossaries =
        new(StringComparer.OrdinalIgnoreCase);

    private static string GlossaryPath =
        Path.Combine(ConfigFolder, "glossary.json");

    private static void LoadGlossary()
    {
        try
        {
            if (!File.Exists(GlossaryPath))
            {
                Console.WriteLine("⚠ glossary.json not found.");
                return;
            }

            var json = File.ReadAllText(GlossaryPath);

            Glossaries = JsonSerializer.Deserialize<
                Dictionary<string, Dictionary<string, string>>
            >(json) ?? new();

            Console.WriteLine("📘 glossary.json loaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Glossary load failed: {ex.Message}");
        }
    }

    // ======================
    // ECHO CONFIG
    // ======================
    private class EchoConfig
    {
        public List<string> Global { get; set; } = new();
        public Dictionary<string, List<string>> Languages { get; set; } = new();
    }

    private static HashSet<string> GlobalEchoExclusions =
        new(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, HashSet<string>> EchoExclusions =
        new(StringComparer.OrdinalIgnoreCase);

    private static string EchoPath =
        Path.Combine(ConfigFolder, "echo.json");

    private static void LoadEchoConfig()
    {
        try
        {
            if (!File.Exists(EchoPath))
            {
                Console.WriteLine("⚠ echo.json not found.");
                return;
            }

            var json = File.ReadAllText(EchoPath);
            var config = JsonSerializer.Deserialize<EchoConfig>(json);

            if (config == null) return;

            GlobalEchoExclusions =
                new HashSet<string>(config.Global ?? new(), StringComparer.OrdinalIgnoreCase);

            EchoExclusions =
                config.Languages?.ToDictionary(
                    k => k.Key,
                    v => new HashSet<string>(v.Value ?? new(), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                ) ?? new();

            Console.WriteLine("📘 echo.json loaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Echo load failed: {ex.Message}");
        }
    }

    // ======================
    // GENERIC HOT RELOAD (DEBOUNCED)
    // ======================
    private static void StartHotReload(
        string filePath,
        ref FileSystemWatcher? watcher,
        Action reloadAction,
        string label,
        int debounceMs = 300)
    {
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath)!)
            {
                Filter = Path.GetFileName(filePath),
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            Timer? timer = null;

            watcher.Changed += (_, __) =>
            {
                // Debounce rapid events
                timer?.Dispose();
                timer = new Timer(_ =>
                {
                    try
                    {
                        reloadAction();
                        Console.WriteLine($"♻ {label} changed — reloaded.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠ {label} reload failed: {ex.Message}");
                    }
                }, null, debounceMs, Timeout.Infinite);
            };

            Console.WriteLine($"👀 {label} hot-reload enabled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ {label} watcher failed: {ex.Message}");
        }
    }

    // ======================
    // CACHE PER LANGUAGE
    // ======================
    private static Dictionary<string, string> Cache = new();
    private static string CurrentCacheFile = "";

    // ======================
    // MAIN
    // ======================
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // ==========================
        // Config & Command-Line Args
        // ==========================
        ForceOverwriteCache = args.Contains("-f");

        List<string> workingResxFolders = new() { ResxFolder };

        // Handle -d folder argument
        var dirArgIndex = Array.FindIndex(args, a => a == "-d");
        if (dirArgIndex >= 0)
        {
            workingResxFolders = new List<string>();
            for (int i = dirArgIndex + 1; i < args.Length && !args[i].StartsWith("-"); i++)
            {
                var inputDir = args[i];
                var match = Directory
                    .GetDirectories(ResxFolder)
                    .FirstOrDefault(d => string.Equals(Path.GetFileName(d), inputDir, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    workingResxFolders.Add(match);
                    Console.WriteLine($"📂 Using subdirectory: {Path.GetFileName(match)}");
                }
                else
                {
                    Console.WriteLine($"⚠ Subdirectory '{inputDir}' not found. Skipping.");
                }
            }
            if (!workingResxFolders.Any()) workingResxFolders.Add(ResxFolder);
        }

        // Handle -p specific resource argument
        List<string>? specificResources = null;
        var pathArgIndex = Array.FindIndex(args, a => a == "-p");
        if (pathArgIndex >= 0)
        {
            specificResources = new List<string>();
            for (int i = pathArgIndex + 1; i < args.Length && !args[i].StartsWith("-"); i++)
                specificResources.Add(args[i]);
            if (specificResources.Any())
                Console.WriteLine($"📌 Translating only resources: {string.Join(", ", specificResources)}");
        }

        // Ensure cache/config folders exist
        Directory.CreateDirectory(CacheFolder);
        Directory.CreateDirectory(ConfigFolder);

        // Load glossary & echo config
        LoadGlossary();
        LoadEchoConfig();
        StartHotReload(GlossaryPath, ref GlossaryWatcher, LoadGlossary, "glossary.json");
        StartHotReload(EchoPath, ref EchoWatcher, LoadEchoConfig, "echo.json");

        // Handle -m model argument
        var modelArgIndex = Array.FindIndex(args, a => a == "-m");
        if (modelArgIndex >= 0 && args.Length > modelArgIndex + 1)
        {
            OllamaModel = $"translategemma:{args[modelArgIndex + 1]}";
            Console.WriteLine($"🔧 Using model: {OllamaModel}");
        }
        else Console.WriteLine($"🔧 Using default model: {OllamaModel}");

        // Handle -l language argument
        List<string> targetLangs = TargetLangs.ToList();
        var langArgIndex = Array.FindIndex(args, a => a == "-l");
        if (langArgIndex >= 0)
        {
            var selectedLangs = new List<string>();
            for (int i = langArgIndex + 1; i < args.Length && !args[i].StartsWith("-"); i++)
            {
                var lang = args[i].ToLower();
                if (Languages.Contains(lang)) selectedLangs.Add(lang);
                else Console.WriteLine($"⚠ Unknown language '{lang}', skipping.");
            }
            if (selectedLangs.Any())
            {
                targetLangs = selectedLangs;
                Console.WriteLine($"🌍 Translating only to: {string.Join(", ", targetLangs)}");
            }
        }

        Console.WriteLine("🚀 Starting translation...");

        // Start Ollama if not running
        if (!await IsOllamaRunning()) await StartOllamaServerAsync();

        // ==========================
        // Concurrency Control
        // ==========================
        var maxConcurrent = 3; // Limit concurrent language translations
        var semaphore = new SemaphoreSlim(maxConcurrent);

        // ==========================
        // Enumerate base .resx files
        // ==========================
        var baseFiles = new List<string>();
        foreach (var folder in workingResxFolders)
        {
            baseFiles.AddRange(
                Directory.EnumerateFiles(folder, "*.resx", SearchOption.AllDirectories)
                .Where(f => string.IsNullOrWhiteSpace(Excluded) || !f.Split(Path.DirectorySeparatorChar).Any(dir => dir.Equals(Excluded, StringComparison.OrdinalIgnoreCase)))
                .Where(f => !Languages.Any(l => f.EndsWith($".{l}.resx", StringComparison.OrdinalIgnoreCase)))
                .Where(f =>
                {
                    if (specificResources == null || !specificResources.Any()) return true;
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(f);
                    return specificResources.Any(r => fileNameWithoutExt.Equals(r, StringComparison.OrdinalIgnoreCase));
                })
            );
        }

        // ==========================
        // Process each base file
        // ==========================
        foreach (var baseFile in baseFiles)
        {
            Console.WriteLine($"\n📄 {Path.GetFileName(baseFile)}");
            var baseDoc = XDocument.Load(baseFile);
            var pageName = Path.GetFileNameWithoutExtension(baseFile);

            // --------------------------
            // Create language tasks
            // --------------------------
            var tasks = targetLangs.Select(async lang =>
            {
                await semaphore.WaitAsync();
                try
                {
                    CurrentCacheFile = Path.Combine(CacheFolder, $"cache_{lang}.json");
                    LoadCache();

                    Console.WriteLine($"🌍 {lang}");
                    var stopwatch = Stopwatch.StartNew();

                    var newDoc = new XDocument(baseDoc);

                    // Get all data elements
                    var dataElements = newDoc.Descendants("data")
                        .Where(d => d.Element("value") != null && !string.IsNullOrWhiteSpace(d.Element("value")!.Value))
                        .ToList();

                    var sources = dataElements.Select(d => d.Element("value")!.Value).ToList();

                    // --------------------------
                    // Step 3: Batch translation
                    // --------------------------
                    var translations = await TranslateBatchAsync(sources, lang, pageName, batchSize: 20);

                    for (int i = 0; i < dataElements.Count; i++)
                        dataElements[i].Element("value")!.Value = translations[i];

                    var outPath = baseFile.Replace(".resx", $".{lang}.resx");
                    newDoc.Save(outPath);
                    SaveCache();

                    stopwatch.Stop();
                    Console.WriteLine($"✅ Written {Path.GetFileName(outPath)} ({stopwatch.Elapsed.TotalSeconds:F2} sec)");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            // Run all languages concurrently (up to maxConcurrent)
            await Task.WhenAll(tasks);
        }

        // ==========================
        // Final logging
        // ==========================
        WriteFinalLog(workingResxFolders, specificResources);
        Console.WriteLine("\n🎉 Done");
        Console.WriteLine("\nPress Enter to exit...");
        Console.ReadLine();
    }


    private static async Task<List<string>> TranslateBatchAsync(List<string> texts, string lang, string pageName, int batchSize = 20)
    {
        var combinedText = string.Join("\n---\n", texts);
        var translatedCombined = await TranslateAsync(combinedText, lang, "batch", pageName);

        var translatedList = translatedCombined?.Split("\n---\n", StringSplitOptions.None)
                            .Select(s => s.Trim())
                            .ToList()
                            ?? Enumerable.Repeat(string.Empty, texts.Count).ToList();

        return translatedList;
    }

    // ======================
    // TRANSLATE
    // ======================
    private static async Task<string?> TranslateAsync(string text, string lang, string key, string pageName)
    {
        if (KeyOverrides.TryGetValue(lang, out var langOverrides) &&
            langOverrides.TryGetValue(key, out var fixedTranslation))
        {
            Console.WriteLine($"🔒 [Override {lang} {key}] {text}\n➡️ {fixedTranslation}");
            Console.WriteLine();

            FinalLog.AppendLine($"{lang} {key} | {fixedTranslation}");
            FinalLog.AppendLine();

            return fixedTranslation;
        }

        var cacheKey = $"{lang}||{text}";

        // ---------- CACHE HIT ----------
        if (Cache.TryGetValue(cacheKey, out var cached) && !ForceOverwriteCache)
        {
            Console.WriteLine($"[Cache hit {lang} {key}] {text}\n➡️ {cached}");
            Console.WriteLine();

            FinalLog.AppendLine($"{lang} {key} | {cached}");
            FinalLog.AppendLine();

            return cached;
        }

        // ---------- TRANSLATION ----------
        var payload = new
        {
            model = OllamaModel,
            prompt = BuildPrompt(text, lang),
            temperature = 0,
            max_tokens = 150
        };

        try
        {
            var response = await Http.PostAsync(
                OllamaUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            );

            response.EnsureSuccessStatusCode();

            var raw = await response.Content.ReadAsStringAsync();
            var result = new StringBuilder();

            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("response", out var r))
                        result.Append(r.GetString());
                }
                catch { }
            }

            var translated = result.ToString().Trim();

            if (!text.EndsWith(".") && !text.EndsWith("!") && !text.EndsWith("?"))
            {
                translated = translated.TrimEnd('.', '!', '?');
            }

            bool echo = IsEnglishEcho(text, translated);
            bool leak = HasScriptLeakage(lang, translated);

            if ((echo && !IsEchoExcluded(lang, text, translated)) || leak)
            {
                if (!ReviewLogExcludedPages.Contains(pageName))
                {
                    Console.WriteLine($"⚠ {pageName} [{lang} {key}]");
                    Console.WriteLine($"   Source: {text}");
                    Console.WriteLine($"   Output: {translated}");
                    Console.WriteLine();

                    WriteReviewLog(pageName, lang, key, text, translated);

                    FinalLog.AppendLine($"⚠ {pageName} [{lang} {key}]");
                    FinalLog.AppendLine($"Source: {text}");
                    FinalLog.AppendLine($"Output: {translated}");
                    FinalLog.AppendLine();
                }
            }

            // ---------- CACHE STORE ----------
            Cache[cacheKey] = translated;

            FinalLog.AppendLine($"{lang} {key} | {translated}");
            FinalLog.AppendLine();

            if (ForceOverwriteCache)
            {
                Console.WriteLine($"♻️ [Rewrite {lang} {key}] {text}\n➡️ {translated}");
            }
            else
            {
                Console.WriteLine($"[Cached {lang} {key}] {text}\n➡️ {translated}");
            }

            Console.WriteLine();

            return translated;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Translation failed [{lang} {key}]: {ex.Message}");
            Console.WriteLine();
            return null;
        }
    }

    // ======================
    // PROMPT
    // ======================
    private static string BuildPrompt(string text, string lang)
    {
        var langName = LangNames.GetValueOrDefault(lang, lang);

        var glossarySection = "";
        if (Glossaries.TryGetValue(lang, out var glossary) && glossary.Any())
        {
            glossarySection = "Use the following glossary: " +
                string.Join(", ", glossary.Select(p => $"\"{p.Key}\" ➡️ \"{p.Value}\"")) + ".";
        }

        string numberInstruction = lang switch
        {
            "km" => "Translate all digits into Khmer numerals (០-៩). For years, use Khmer numerals in AD format (e.g., '2024' to '២០២៤').",
            "zh" or "ja" => "Use Arabic numerals for years (e.g., '2024年'). For other numbers, use native characters if appropriate for formal context, otherwise maintain Arabic numerals.",
            "th" => "Translate all digits into Thai numerals. Convert years to Buddhist Era (add 543) before translating digits.",
            "lo" => "Translate all digits into Lao numerals (໐-໙). Convert years to Buddhist Era (add 543) before translating digits.",
            "fr" or "de" or "it" or "es" or "pt" or "ru" or "sv" or "nl" or "cs" =>
                $"For {langName}: Use Arabic numerals. Use a space or dot for thousands and a comma for decimals (European style).",
            "vi" => "Use Arabic numerals: use a dot (.) for thousands and a comma (,) for decimals. For years, always include the word 'năm' (e.g., 'năm 2024').",
            "hi" => "Use standard Arabic numerals (0-9). Devanagari numerals are not required for this modern UI context.",
            _ => "Maintain standard Arabic numerals and original numeric formatting."
        };

        string styleInstruction = lang switch
        {
            "de" or "nl" or "sv" => 
                $"For {langName}: Avoid hyphens between nouns. Use natural spaces or compound words (e.g., 'Durian Kreisverkehr', NOT 'Durian-Kreisverkehr', 'Bokor Berg' NOT 'Bokor-Berg', 'Tourismus Hafen' NOT 'Tourismus-Hafen').",
            _ => ""
        };

        return $"""
        You are a professional English translator to {langName} specializing in Software Resource Files (.resx).
        Translate UI strings and labels accurately, maintaining the original meaning and technical style.

        RULES:
        - {numberInstruction}
        - {styleInstruction}
        - Keep translations concise to fit UI elements.
        - Do not add any punctuation at the end that is not present in the original text.
        - If the input is only a name or short phrase, produce it naturally without commas.
        - Produce ONLY the translation. No explanations or commentary.
        - Do NOT include any English words in the output unless they are proper nouns.
        - The output must be fully written in {langName}.
        {glossarySection}


        {text}
        """;
    }

    // ======================
    // CHECK / START OLLAMA
    // ======================
    private static async Task<bool> IsOllamaRunning()
    {
        try
        {
            var payload = new
            {
                model = OllamaModel,
                prompt = "ping",
                temperature = 0,
                max_tokens = 1
            };

            var response = await Http.PostAsync(
                OllamaUrl,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            );

            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task StartOllamaServerAsync(int timeoutSeconds = 20)
    {
        if (OllamaProcess != null && !OllamaProcess.HasExited)
        {
            Console.WriteLine("⚡ Ollama already running.");
            return;
        }

        Console.WriteLine("⚡ Starting Ollama server...");
        
        OllamaProcess = new Process();
        OllamaProcess.StartInfo.FileName = "cmd.exe";
        OllamaProcess.StartInfo.Arguments = "/c ollama serve";
        OllamaProcess.StartInfo.CreateNoWindow = true;
        OllamaProcess.StartInfo.UseShellExecute = false;
        OllamaProcess.Start();

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            if (await IsOllamaRunning())
            {
                Console.WriteLine("✅ Ollama server is ready.");
                return;
            }
            await Task.Delay(500);
        }

        Console.WriteLine("⚠ Timeout waiting for Ollama server. It may not be ready.");
    }

    private static void StopOllama()
    {
        try
        {
            if (OllamaProcess != null && !OllamaProcess.HasExited)
            {
                Console.WriteLine("🛑 Stopping Ollama server...");
                OllamaProcess.Kill();
                OllamaProcess.WaitForExit(3000); // optional wait for cleanup
            }
        }
        catch { }
    }

    // ======================
    // ECHO DETECTION
    // ======================
    private static bool IsEnglishEcho(string src, string trg)
    {
        string Normalize(string s) => string.Join(" ", s.ToLower().Split());
        var s = Normalize(src);
        var t = Normalize(trg);

        if (s == t)
            return true;

        int sameChars = s.Zip(t, (a, b) => a == b ? 1 : 0).Sum();
        double similarity = (double)sameChars / Math.Max(s.Length, t.Length);

        return similarity > 0.9;
    }

    private static bool HasScriptLeakage(string lang, string text)
    {
        return lang switch
        {
            "km" or "lo" or "th" or "ru" or "hi" or "zh" or "ja" or "ko"
                => Regex.IsMatch(text, "[A-Za-z]"),
            _ => false
        };
    }

    private static bool IsEchoExcluded(string lang, string source, string translated)
    {
        var src = source.Trim();
        var trg = translated.Trim();

        if (!string.Equals(src, trg, StringComparison.OrdinalIgnoreCase))
            return false;

        if (GlobalEchoExclusions.Contains(src))
            return true;

        if (EchoExclusions.TryGetValue(lang, out var set)
            && set.Contains(src))
            return true;

        return false;
    }

    // ======================
    // REVIEW LOG
    // ======================
    private static void WriteReviewLog(string pageName, string lang, string key, string source, string output)
    {
        try
        {
            var entry = new StringBuilder();
            entry.AppendLine($"⚠ {pageName} [{lang} {key}]");
            entry.AppendLine($"Source: {source}");
            entry.AppendLine($"Output: {output}");
            entry.AppendLine(new string('-', 60));

            File.AppendAllText(ReviewLogPath, entry.ToString(), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to write review log: {ex.Message}");
        }
    }

    // ======================
    // FINAL LOG
    // ======================

    private static void WriteFinalLog(List<string> folders, List<string>? resources)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            string name;

            if (resources != null && resources.Any())
            {
                name = string.Join("_", resources);
            }
            else if (folders != null && folders.Count == 1)
            {
                name = Path.GetFileName(folders.First());
            }
            else
            {
                name = "FullTranslation";
            }

            var fileName = $"{name}.log";
            var fullPath = Path.Combine(desktopPath, fileName);

            File.WriteAllText(fullPath, FinalLog.ToString(), Encoding.UTF8);

            Console.WriteLine($"📝 Log written to: {fullPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to write final log: {ex.Message}");
        }
    }

    // ======================
    // CACHE HANDLING PER LANGUAGE
    // ======================
    private static void LoadCache()
    {
        if (!string.IsNullOrEmpty(CurrentCacheFile) && File.Exists(CurrentCacheFile))
        {
            try
            {
                Cache = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(CurrentCacheFile)
                ) ?? new Dictionary<string, string>();

                Console.WriteLine($"🗂 Loaded cache [{Path.GetFileName(CurrentCacheFile)}] with {Cache.Count} entries");
                Console.WriteLine();
            }
            catch
            {
                Cache = new Dictionary<string, string>();
                Console.WriteLine($"⚠ Failed to read cache [{Path.GetFileName(CurrentCacheFile)}], starting fresh");
                Console.WriteLine();
            }
        }
        else
        {
            Cache = new Dictionary<string, string>();
            Console.WriteLine($"🗂 No existing cache [{Path.GetFileName(CurrentCacheFile)}], starting fresh");
            Console.WriteLine();
        }
    }

    private static void SaveCache()
    {
        if (string.IsNullOrEmpty(CurrentCacheFile)) return;

        try
        {
            File.WriteAllText(
                CurrentCacheFile,
                JsonSerializer.Serialize(Cache, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                })
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠ Failed to save cache [{Path.GetFileName(CurrentCacheFile)}]: {ex.Message}");
        }
    }
}