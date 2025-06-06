using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CrestCreates.Infrastructure.Localization
{
    public class JsonResourceLocalizationProvider : ILocalizationProvider
    {
        private readonly Dictionary<string, Dictionary<string, string>> _resources;
        private readonly CultureInfo _defaultCulture;
        
        public JsonResourceLocalizationProvider(string resourcePath, CultureInfo? defaultCulture = null)
        {
            _resources = new Dictionary<string, Dictionary<string, string>>();
            _defaultCulture = defaultCulture ?? CultureInfo.CurrentCulture;
            
            LoadResources(resourcePath);
        }
        
        private void LoadResources(string resourcePath)
        {
            if (!Directory.Exists(resourcePath))
                throw new DirectoryNotFoundException($"Resource directory not found: {resourcePath}");
                
            foreach (var file in Directory.GetFiles(resourcePath, "*.json"))
            {
                var cultureName = Path.GetFileNameWithoutExtension(file);
                var jsonContent = File.ReadAllText(file);
                var resourceDict = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
                
                _resources[cultureName] = resourceDict;
            }
        }
        
        public string GetString(string name)
        {
            return GetString(name, CultureInfo.CurrentCulture);
        }

        public string GetString(string name, params object[] args)
        {
            return GetString(name, CultureInfo.CurrentCulture, args);
        }

        public string GetString(string name, CultureInfo culture)
        {
            if (TryGetResource(name, culture, out string value))
                return value;
                
            // 回退到默认文化
            if (culture.Name != _defaultCulture.Name)
            {
                if (TryGetResource(name, _defaultCulture, out value))
                    return value;
            }
            
            // 如果没有找到资源，返回键名
            return name;
        }

        public string GetString(string name, CultureInfo culture, params object[] args)
        {
            string value = GetString(name, culture);
            return string.Format(value, args);
        }
        
        private bool TryGetResource(string name, CultureInfo culture, out string value)
        {
            value = null;
            
            if (_resources.TryGetValue(culture.Name, out var resources))
            {
                if (resources.TryGetValue(name, out value))
                    return true;
            }
            
            // 尝试不区分方言的文化（如en-US -> en）
            if (culture.Parent != null && !culture.IsNeutralCulture)
            {
                if (_resources.TryGetValue(culture.Parent.Name, out resources))
                {
                    if (resources.TryGetValue(name, out value))
                        return true;
                }
            }
            
            return false;
        }
    }
}
