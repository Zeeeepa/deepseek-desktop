namespace DeepSeekBrowser.Services.Harness;

public static class HarnessEmptyReply
{
    public static bool IsEmpty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var t = text.Trim();
        return t.Equals("(无回复)", StringComparison.Ordinal)
               || t.Equals("（无回复）", StringComparison.Ordinal)
               || t.Equals("(无回复内容)", StringComparison.Ordinal)
               || t.Equals("（无回复内容）", StringComparison.Ordinal);
    }
}
