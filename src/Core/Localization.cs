namespace Frosty;

public static class Loc
{
    public static bool IsEnglish { get; private set; }
    public static event Action? LanguageChanged;

    public static void SetLanguage(bool isEnglish)
    {
        if (IsEnglish == isEnglish) return;
        IsEnglish = isEnglish;
        LanguageChanged?.Invoke();
    }

    public static string Tr(string ru, string en) => IsEnglish ? en : ru;
}
