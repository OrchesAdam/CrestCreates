using System;
using System.Globalization;
using System.Resources;

namespace CrestCreates.Infrastructure.Localization
{
    public interface ILocalizationProvider
    {
        string GetString(string name);
        string GetString(string name, params object[] args);
        string GetString(string name, CultureInfo culture);
        string GetString(string name, CultureInfo culture, params object[] args);
    }

    public class ResourceManagerLocalizationProvider : ILocalizationProvider
    {
        private readonly ResourceManager _resourceManager;
        
        public ResourceManagerLocalizationProvider(ResourceManager resourceManager)
        {
            _resourceManager = resourceManager;
        }
        
        public string GetString(string name)
        {
            return _resourceManager.GetString(name);
        }

        public string GetString(string name, params object[] args)
        {
            string value = GetString(name);
            if (value == null) return null;
            
            return string.Format(value, args);
        }

        public string GetString(string name, CultureInfo culture)
        {
            return _resourceManager.GetString(name, culture);
        }

        public string GetString(string name, CultureInfo culture, params object[] args)
        {
            string value = GetString(name, culture);
            if (value == null) return null;
            
            return string.Format(value, args);
        }
    }
}
