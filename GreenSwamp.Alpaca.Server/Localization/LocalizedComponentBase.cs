using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace GreenSwamp.Alpaca.Server.Localization
{
    /// <summary>
    /// Base for every component (applied through _Imports.razor). Provides <see cref="Loc"/> for UI text
    /// and makes each re-render and <see cref="InvokeAsync(Action)"/> work item run on this browser
    /// circuit's cultures rather than those of the server thread that raised the event.
    /// </summary>
    public abstract class LocalizedComponentBase : ComponentBase
    {
        [Inject]
        private CircuitCulture CircuitCultureService { get; set; } = default!;

        /// <summary>UI text in the current display language. Keys are scoped, e.g. <c>MountStatus_ParkButton</c>.</summary>
        [Inject]
        protected IStringLocalizer<SharedResource> Loc { get; set; } = default!;

        /// <summary>Called by StateHasChanged just before a render is queued; the render runs in the same flow.</summary>
        protected override bool ShouldRender()
        {
            CircuitCultureService.Apply();
            return true;
        }

        /// <summary>Runs the work item on the renderer using this circuit's cultures.</summary>
        protected new Task InvokeAsync(Action workItem) => base.InvokeAsync(() =>
        {
            using (CircuitCultureService.Enter())
            {
                workItem();
            }
        });

        /// <summary>Runs the work item on the renderer using this circuit's cultures.</summary>
        protected new Task InvokeAsync(Func<Task> workItem) => base.InvokeAsync(async () =>
        {
            using (CircuitCultureService.Enter())
            {
                await workItem();
            }
        });
    }
}
