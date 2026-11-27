namespace BodycamHorror.UI
{
    /// <summary>
    /// The menus' language (main menu, pause menu, settings). Picked on the settings' Общее / General tab and kept
    /// by <see cref="GameSettings.Language"/>. Every menu word is written in place as <c>Loc.T("Русский", "English")</c>,
    /// so a translation stays next to what it translates.
    /// </summary>
    public static class Loc
    {
        public enum Language { Russian, English }

        /// <summary>Names of the languages as the settings show them, each in its own language.</summary>
        public static readonly string[] Names = { "Русский", "English" };

        public static Language Current => GameSettings.Language;
        public static bool English => Current == Language.English;

        /// <summary>The Russian or the English words, for the current language.</summary>
        public static string T(string ru, string en) => English ? en : ru;
    }
}
