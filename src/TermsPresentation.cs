using System;
using System.Text.RegularExpressions;

internal static class TermsPresentation {
    // The Windows multiline textbox expects CRLF, while the source may use LF.
    internal static string Format(string text){return Regex.Replace(text??String.Empty,@"\r\n|\r|\n","\r\n");}
}
