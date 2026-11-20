using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Globalization;

namespace ASCOM.Alpaca.Razor
{
    internal sealed class ClientTransactionIdModelBinderProvider : IModelBinderProvider
    {
        private static readonly IModelBinder Binder = new ClientTransactionIdModelBinder();

        public IModelBinder? GetBinder(ModelBinderProviderContext context)
        {
            var source = context.BindingInfo.BindingSource;
            return context.Metadata.ModelType == typeof(uint) &&
                context.Metadata.ParameterName == "ClientTransactionID" &&
                (source == BindingSource.Query || source == BindingSource.Form)
                ? Binder
                : null;
        }

        private sealed class ClientTransactionIdModelBinder : IModelBinder
        {
            public Task BindModelAsync(ModelBindingContext bindingContext)
            {
                var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
                uint transactionId = 0;
                if (value != ValueProviderResult.None)
                {
                    bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
                    if (!uint.TryParse(value.FirstValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out transactionId))
                    {
                        Logging.LogVerbose("Invalid Alpaca ClientTransactionID; using 0.");
                    }
                }

                bindingContext.Result = ModelBindingResult.Success(transactionId);
                return Task.CompletedTask;
            }
        }
    }
}
