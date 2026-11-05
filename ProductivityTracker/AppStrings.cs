using System;
using System.Globalization;
using System.Linq;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.Globalization;

namespace ProductivityTracker
{
    internal static class AppStrings
    {
        private static readonly Lazy<ResourceLoader> Loader = new(() => new ResourceLoader());
        private static readonly string[] SupportedLanguages =
            { "en-US", "hi-IN", "zh-CN", "ar-SA", "he-IL", "es-ES", "fr-FR", "ko-KR", "ja-JP" };

        public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentCulture;

        public static void Initialize()
        {
            var requested = Environment.GetCommandLineArgs()
                .FirstOrDefault(argument => argument.StartsWith("--language=", StringComparison.OrdinalIgnoreCase))
                ?.Split('=', 2)[1];
            if (requested != null && !SupportedLanguages.Contains(requested, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Unsupported --language value: " + requested);
            }
            if (requested != null)
            {
                ApplicationLanguages.PrimaryLanguageOverride = requested;
            }
            var culture = (CultureInfo)CultureInfo.GetCultureInfo(Get("LanguageTag")).Clone();
            var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().FirstOrDefault();
            if (gregorian != null)
            {
                culture.DateTimeFormat.Calendar = gregorian;
            }
            Culture = culture;
            CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public static string Get(string key) => Loader.Value.GetString(key);

        public static string Format(string key, params object[] values) =>
            string.Format(Culture, Get(key), values);

        public static string Date(DateTime value, string formatKey) =>
            value.ToString(Get(formatKey), Culture);

        public static string Isolate(string value, bool leftToRight = false) =>
            Culture.TextInfo.IsRightToLeft ? (leftToRight ? "\u2066" : "\u2068") + value + "\u2069" : value;
    }
}
