using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace GreenSwamp.Alpaca.Server.Localization
{
    /// <summary>
    /// Layout equivalent of <see cref="LocalizedComponentBase"/>: provides <see cref="Loc"/> and keeps
    /// renders and <see cref="InvokeAsync(Action)"/> work on this browser circuit's cultures.
    /// </summary>
    public abstract class LocalizedLayoutComponentBase : LayoutComponentBase
    {
        [Inject]
        private CircuitCulture CircuitCultureService { get; set; } = default!;

        /// <summary>UI text in the current display language.</summary>
        [Inject]
        protected IStringLocalizer<SharedResource> Loc { get; set; } = default!;

        protected override bool ShouldRender()
        {
            CircuitCultureService.Apply();
            return true;
        }

        protected new Task InvokeAsync(Action workItem) => base.InvokeAsync(() =>
        {
            using (CircuitCultureService.Enter())
            {
                workItem();
            }
        });

        protected new Task InvokeAsync(Func<Task> workItem) => base.InvokeAsync(async () =>
        {
            using (CircuitCultureService.Enter())
            {
                await workItem();
            }
        });
    }
}
