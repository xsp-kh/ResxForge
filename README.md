<h1>ResxForge</h1>

<p>
<strong>Open <code>Program.cs</code> and update the paths below to match your folder structure:</strong>
</p>

<pre>
private const string ResxFolder = @"C:\Users\xxx\source\repos\ResxForge\Resources";
private const string ConfigFolder = @"C:\Users\xxx\source\repos\ResxForge\config";
private const string CacheFolder = @"C:\Users\xxx\source\repos\ResxForge\cache";
</pre>

<p>
<strong>Open <code>ResxForge.csproj</code> to change TargetFramework</strong>
</p>

<pre>
<TargetFramework>net10.0</TargetFramework>
</pre>

![console app](cmd.jpg)

<p>
<strong>Deterministic, glossary-aware localization pipeline for <code>.resx</code> files powered by local LLMs (via Ollama).</strong>
</p>

<p>
ResxForge automates translation of .NET resource files while maintaining control, consistency, and UI-safe rules.
</p>

<div class="section">

<h2>Why ResxForge?</h2>

<p>Traditional LLM translation workflows:</p>
<ul>
    <li>Are non-deterministic</li>
    <li>Depend on cloud APIs</li>
    <li>Lack glossary enforcement</li>
    <li>Do not understand UI constraints</li>
</ul>

<p><strong>ResxForge adds:</strong></p>
<ul>
    <li>🔒 Key-level overrides</li>
    <li>📘 Language-specific glossaries (hot reload supported)</li>
    <li>🔁 Per-language cache system</li>
    <li>🔍 English echo detection</li>
    <li>🧠 Script leakage detection</li>
    <li>🔢 Language-aware numeric formatting rules</li>
    <li>⚡ Automatic Ollama bootstrapping</li>
    <li>📝 Translation review logging</li>
</ul>

<p>
Designed specifically for <code>.resx</code> UI localization —
not generic document translation.
</p>
</div>

<div class="section">
<h2>Requirements</h2>
<ul>
    <li>.NET 7+ (or your target version)</li>
    <li>Ollama installed and available in PATH</li>
    <li>A compatible local translation model (e.g. translategemma)</li>
</ul>
</div>

<div class="section">
<h2>Usage</h2>

<h3>Basic</h3>
<pre><code>dotnet run</code></pre>

<h3>Translate specific languages</h3>
<pre><code>dotnet run -- -l zh fr</code></pre>

<h3>Translate specific resource pages</h3>
<pre><code>dotnet run -- -p Index pepper</code></pre>

<h3>Build</h3>
<pre>
<code>dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true</code>
</pre>

batch and batch-parallel available

<pre>
<code>dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:AppVariant=batch</code>
<code>dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:AppVariant=batch-parallel</code>
</pre>

<h3>Force cache rewrite</h3>
<pre><code>dotnet run -- -f</code></pre>

</div>

<div class="section">
<h2>Configuration</h2>

<h3>Glossary</h3>
<p>Location: <code>/config/glossary.json</code></p>

<pre><code>{
  "de": {
    "City Hall": "Rathaus"
  }
}</code></pre>

<p>Supports hot reload while the application is running.</p>

<h3>Echo Rules</h3>
<p>Location: <code>/config/echo.json</code></p>
<p>Define phrases that should not trigger echo detection warnings.</p>

<pre><code>{
  "global": [
    "(FAQ)"
  ],
  "languages": {
    "zh": [],
    "vi": [],
    "th": [],
    "de": [Mango]
  }
}</code></pre>

<h3>Echo Detection & Script Safety</h3>

<p>
ResxForge detects when a translation is identical (or nearly identical) to the English source.
This helps prevent untranslated UI strings.
</p>

<p>
Echo exclusions can be defined in <code>echo.json</code> to suppress warnings
for intentionally identical terms (e.g., brand names).
</p>

<p>
<strong>Important:</strong> Echo exclusions do not bypass script validation.
For non-Latin languages such as Korean, Japanese, Chinese, Khmer, Thai, Lao, Russian, or Hindi,
translations containing Latin alphabet characters (A–Z) will still trigger a script leakage warning,
even if the phrase is listed as an echo exception.
</p>

<p>
This ensures UI integrity and prevents accidental English leakage in non-Latin locales.
</p>

</div>

<div class="section">
<h2>Caching</h2>

<p>Each language maintains its own JSON cache:</p>

<pre><code>cache_km.json
cache_fr.json
cache_zh.json</code></pre>

<p>Ensures:</p>
<ul>
    <li>Deterministic re-runs</li>
    <li>Faster incremental translations</li>
    <li>Review traceability</li>
</ul>
</div>

<div class="section">
<h2>Philosophy</h2>
<ul>
    <li><strong>Local-first</strong> — no cloud lock-in</li>
    <li><strong>Deterministic workflows</strong> — reproducible outputs</li>
    <li><strong>UI-safe translations</strong> — optimized for interface strings</li>
</ul>
</div>

<div class="section">
<h2>Roadmap</h2>
<ul>
    <li>Diff review mode</li>
    <li>CI integration</li>
    <li>Interactive review console</li>
    <li>NuGet packaging</li>
    <li>Optional GUI frontend</li>
</ul>
</div>

<div class="section">
<h2>Support</h2>
<p>
If this tool saved you time, you can support development:
</p>

<ul>
    <li>☕ Buy Me a Coffee: https://buymeacoffee.com/xinsu</li>
    <li>🪙 Monero: 8BWYhttoHAb961iAVw7mkHArtioB6SdwhMyV89nh7uTcKZ1C2MiQq3kJVoEKz4YCDn6HEX1t1SCxHgzbVdRoVQ1jP7rb3Vi</li>
</ul>
</div>

<footer>
MIT License<br>
© 2026 ResxForge
</footer>
