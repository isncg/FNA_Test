namespace FNA.Gui
{
    /// <summary>
    /// String localization provider. Default implementation uses an
    /// in-memory dictionary; can be replaced with a resource-file loader.
    /// </summary>
    public interface ILocalization
    {
        /// <summary>Get a localized string by key. Returns the key itself if not found.</summary>
        string Get(string key);

        /// <summary>Get a localized string with format arguments.</summary>
        string Get(string key, params object[] args);
    }

    /// <summary>Simple in-memory localization backed by a dictionary.</summary>
    public class DictionaryLocalization : ILocalization
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _dict;

        public DictionaryLocalization(System.Collections.Generic.Dictionary<string, string>? dict = null)
        {
            _dict = dict ?? new System.Collections.Generic.Dictionary<string, string>();
        }

        public void Set(string key, string value) => _dict[key] = value;

        public string Get(string key) =>
            _dict.TryGetValue(key, out var value) ? value : key;

        public string Get(string key, params object[] args) =>
            string.Format(Get(key), args);
    }
}
