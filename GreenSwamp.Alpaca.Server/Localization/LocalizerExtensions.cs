using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace GreenSwamp.Alpaca.Server.Localization
{
    public static class LocalizerExtensions
    {
        /// <summary>
        /// Localized text that contains trusted markup (for example <c>&lt;strong&gt;</c>) in the resource.
        /// Arguments are HTML-encoded before they are inserted, so device names and other user data are safe.
        /// Pass a <see cref="MarkupString"/> argument to insert markup unencoded. Avoid format specifiers
        /// such as <c>{0:N2}</c> in these resources.
        /// </summary>
        public static MarkupString Html(this IStringLocalizer localizer, string key, params object?[] args)
        {
            var encoded = new object?[args.Length];
            for (var i = 0; i < args.Length; i++)
            {
                encoded[i] = args[i] is MarkupString markup
                    ? markup.Value
                    : WebUtility.HtmlEncode(Convert.ToString(args[i], CultureInfo.CurrentCulture));
            }

            return new MarkupString(localizer[key, encoded!].Value);
        }
    }
}
