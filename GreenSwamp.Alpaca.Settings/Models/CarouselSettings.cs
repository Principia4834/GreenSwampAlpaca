/* Copyright(C) 2019-2026 Rob Morgan (robert.morgan.e@gmail.com)

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published
    by the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

namespace GreenSwamp.Alpaca.Settings.Models
{
    /// <summary>
    /// Root object for carousel.settings.json. Drives the home page image carousel.
    /// Not shown in Settings Explorer; edited directly by the developer.
    /// </summary>
    public class CarouselSettings
    {
        public const int MinDwellSeconds = 2;
        public const int MaxDwellSeconds = 60;
        public const int DefaultDwellSeconds = 5;

        /// <summary>Seconds each slide is shown (including the 0.5 s fade). Clamped to 2-60 on read.</summary>
        public int DwellSeconds { get; set; } = DefaultDwellSeconds;

        /// <summary>Id of the show last selected on the home page.</summary>
        public string ActiveShow { get; set; } = "reference";

        public List<CarouselShow> Shows { get; set; } = [];

        /// <summary>Factory defaults: two empty shows. Slides are added by editing carousel.settings.json.</summary>
        public static CarouselSettings CreateDefault() => new()
        {
            ActiveShow = "reference",
            Shows =
            [
                new CarouselShow { Id = "reference", Title = "Server Reference", Folder = "reference" },
                new CarouselShow { Id = "astronomy", Title = "Astronomical Images", Folder = "astronomy" }
            ]
        };

        /// <summary>
        /// Clamps the dwell time, removes shows/slides whose folder or file names are not plain
        /// names (path traversal protection) and falls back to the first show if ActiveShow is unknown.
        /// Each correction is reported through <paramref name="log"/>.
        /// </summary>
        public CarouselSettings Sanitize(Action<string>? log = null)
        {
            var clamped = Math.Clamp(DwellSeconds, MinDwellSeconds, MaxDwellSeconds);
            if (clamped != DwellSeconds)
            {
                log?.Invoke($"Carousel DwellSeconds {DwellSeconds} out of range; using {clamped}");
                DwellSeconds = clamped;
            }

            Shows ??= [];
            Shows.RemoveAll(s =>
            {
                if (s is null || string.IsNullOrWhiteSpace(s.Id) || !IsPlainName(s.Folder))
                {
                    log?.Invoke($"Carousel show '{s?.Id}' ignored: invalid id or folder '{s?.Folder}'");
                    return true;
                }
                return false;
            });

            foreach (var show in Shows)
            {
                show.Slides ??= [];
                show.Slides.RemoveAll(sl =>
                {
                    if (sl is null || !IsPlainName(sl.File))
                    {
                        log?.Invoke($"Carousel slide ignored in show '{show.Id}': invalid file name '{sl?.File}'");
                        return true;
                    }
                    return false;
                });
            }

            if (Shows.Count > 0 && !Shows.Any(s => string.Equals(s.Id, ActiveShow, StringComparison.OrdinalIgnoreCase)))
                ActiveShow = Shows[0].Id;

            return this;
        }

        /// <summary>True for a simple file or folder name with no path separators, drive or '..'.</summary>
        public static bool IsPlainName(string? name) =>
            !string.IsNullOrWhiteSpace(name)
            && name == name.Trim()
            && name != "." && name != ".."
            && name.IndexOfAny(['/', '\\', ':']) < 0
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    /// <summary>A named set of slides. Images live in wwwroot/images/carousel/{Folder}/.</summary>
    public class CarouselShow
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Folder { get; set; } = string.Empty;
        public List<CarouselSlide> Slides { get; set; } = [];
    }

    /// <summary>A single jpg image and its one-line caption.</summary>
    public class CarouselSlide
    {
        public string File { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
    }
}
