using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeadEditor.Services
{
    /// <summary>
    /// Provides autocomplete suggestions for album/release names from Data/releases.json.
    /// Lazily loaded singleton. Supports adding new standalone releases and persisting.
    /// </summary>
    public class ReleaseLookupService
    {
        private static readonly Lazy<ReleaseLookupService> _instance = new(() => new ReleaseLookupService());
        public static ReleaseLookupService Instance => _instance.Value;

        private readonly string _filePath;
        private readonly List<string> _allNames;
        private readonly HashSet<string> _knownNames;
        private JObject? _rawJson;

        private ReleaseLookupService()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "releases.json");
            _allNames = new List<string>();
            _knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_filePath))
            {
                Console.WriteLine($"[ReleaseLookupService] Warning: releases.json not found at {_filePath}");
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                _rawJson = JObject.Parse(json);

                // Expand series templates
                var series = _rawJson["series"] as JArray;
                if (series != null)
                {
                    foreach (var s in series)
                    {
                        var template = s.Value<string>("template") ?? "";
                        var volumes = s["volumes"] as JArray;
                        if (volumes == null) continue;

                        foreach (var v in volumes)
                        {
                            string name;
                            if (v.Type == JTokenType.Object)
                            {
                                // Road Trips style: {"vol": 1, "num": 1}
                                var vol = v.Value<int>("vol");
                                var num = v.Value<int>("num");
                                name = template.Replace("{N}", vol.ToString()).Replace("{M}", num.ToString());
                            }
                            else
                            {
                                // Simple integer volume
                                name = template.Replace("{N}", v.ToString());
                            }
                            _allNames.Add(name);
                            _knownNames.Add(name);
                        }
                    }
                }

                // Add standalone names
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone != null)
                {
                    foreach (var item in standalone)
                    {
                        var name = item.Value<string>() ?? "";
                        if (!string.IsNullOrEmpty(name))
                        {
                            _allNames.Add(name);
                            _knownNames.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReleaseLookupService] Error loading releases.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns up to 20 release names matching the search text (case-insensitive).
        /// Prefix matches sort before contains matches.
        /// Returns empty list for empty/short search text.
        /// </summary>
        public List<string> GetSuggestions(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText) || searchText.Length < 2)
                return new List<string>();

            var search = searchText.Trim();
            var prefixMatches = new List<string>();
            var containsMatches = new List<string>();

            foreach (var name in _allNames)
            {
                if (name.StartsWith(search, StringComparison.OrdinalIgnoreCase))
                    prefixMatches.Add(name);
                else if (name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    containsMatches.Add(name);
            }

            return prefixMatches.Concat(containsMatches).Take(20).ToList();
        }

        /// <summary>
        /// Returns true if the name matches a known release exactly (case-insensitive).
        /// </summary>
        public bool IsKnownRelease(string name)
        {
            return !string.IsNullOrEmpty(name) && _knownNames.Contains(name);
        }

        /// <summary>
        /// Adds a new standalone release name and persists to releases.json.
        /// </summary>
        public void AddRelease(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || _knownNames.Contains(name))
                return;

            _allNames.Add(name);
            _knownNames.Add(name);

            // Add to JSON standalone array
            if (_rawJson != null)
            {
                var standalone = _rawJson["standalone"] as JArray;
                if (standalone == null)
                {
                    standalone = new JArray();
                    _rawJson["standalone"] = standalone;
                }
                standalone.Add(name);
            }

            SaveToFile();
        }

        /// <summary>
        /// Persists the current state back to releases.json.
        /// </summary>
        public void SaveToFile()
        {
            if (_rawJson == null) return;

            try
            {
                var json = _rawJson.ToString(Formatting.Indented);
                var tempPath = _filePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _filePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReleaseLookupService] Error saving releases.json: {ex.Message}");
            }
        }
    }
}
