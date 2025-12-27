using ASCOM.Alpaca.Razor;
using ASCOM.Common;
using ASCOM.Common.Alpaca;
using ASCOM.Common.DeviceInterfaces;
using ASCOM.Common.Helpers;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace ASCOM.Alpaca
{
    public class ProcessBaseController : Controller
    {
        /// <summary>
        /// Validates the HTTP request for compliance with the Alpaca protocol and identifies bad requests.
        /// </summary>
        /// <remarks>This method checks for various conditions that violate the Alpaca protocol, such as
        /// incorrect URL capitalization, invalid form keys, or the presence of both form and query parameters in the
        /// same request. If a violation is detected, it logs the error and sets the <paramref name="Result"/> parameter
        /// to describe the issue.</remarks>
        /// <param name="Result">When the method returns <see langword="true"/>, this parameter contains a <see
        /// cref="BadRequestObjectResult"/> describing the error. Otherwise, it is <see langword="null"/>.</param>
        /// <param name="ClientID">A reference to the client identifier. If an optional key validation fails, this value may be set to 0.</param>
        /// <param name="ClientTransactionID">A reference to the client transaction identifier. If an optional key validation fails, this value may be set
        /// to 0.</param>
        /// <returns><see langword="true"/> if the request is determined to be invalid according to the Alpaca protocol;
        /// otherwise, <see langword="false"/>.</returns>
        private bool BadRequestAlpacaProtocol(out BadRequestObjectResult Result, ref uint ClientID, ref uint ClientTransactionID)
        {
            Result = null;
            //Only check on Alpaca routes, all others may pass
            if (!HttpContext.Request.Path.ToString().Contains("api/"))
            {
                return false;
            }

            if (HttpContext.Request.Path.ToString().Any(char.IsUpper))
            {
                Result = BadRequest(Strings.URLCapitalizationDescription + HttpContext.Request.Path.ToString());
                Logging.LogError($"Error on request {HttpContext.Request.Path} with details: {Result.Value?.ToString()}");
                return true;
            }

            if (HttpContext.Request.HasFormContentType)
            {
                foreach (var key in HttpContext.Request.Form.Keys)
                {
                    var Validator = ValidAlpacaKeys.AlpacaFormValidators.FirstOrDefault(x => x.ExternalKeyFailsValidation(key), null);

                    if (Validator != null)
                    {
                        Logging.LogWarning($"Incorrect capitalization on optional key {Validator.Key}, received {key}");
                        //We zero out optional keys
                        if (Validator.IsOptional)
                        {
                            if (Validator.Key == "ClientID")
                            {
                                ClientID = 0;
                            }
                            else if (Validator.Key == "ClientTransactionID")
                            {
                                ClientTransactionID = 0;
                            }
                        }
                        else
                        {
                            Result = BadRequest(Strings.FormCapitalizationDescription + $"{Validator.Key}, received: {key}");
                            Logging.LogError($"Error on request {HttpContext.Request.Path} with details: {Result.Value?.ToString()}");
                            return true;
                        }
                    }
                }

                if (HttpContext.Request.Query.Count > 0)
                {
                    var keys = HttpContext.Request.Query.Keys;
                    Result = BadRequest(Strings.FormWithQueryDescription + string.Join(", ", keys));
                    Logging.LogError($"Error on request {HttpContext.Request.Path} with details: {Result.Value?.ToString()}");
                    return true;
                }
            }

            if (HttpContext.Request.Method == "GET" && HttpContext.Request.HasFormContentType && HttpContext.Request.Form.Keys.Count > 0)
            {
                var keys = HttpContext.Request.Form.Keys;
                Result = BadRequest(Strings.QueryWithFormDescription + string.Join(", ", keys));
                Logging.LogError($"Error on request {HttpContext.Request.Path} with details: {Result.Value?.ToString()}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the device cannot accept the requested operation based on the current HTTP request path
        /// and device state.
        /// </summary>
        /// <remarks>This method evaluates the HTTP request path to extract device-related information,
        /// such as the device type, device number, and requested operation. It then checks if the specified device
        /// exists and whether it is in a state to accept the operation.</remarks>
        /// <returns><see langword="true"/> if the device cannot accept the requested operation; otherwise, <see
        /// langword="false"/>.</returns>
        private bool DeviceCannotAcceptOperation()
        {
            // /api/v1/{deviceType}/{deviceNumber}/{operation}/...
            var pathElements = HttpContext.Request.Path.ToString().Split('/');
            if (pathElements.Length < 6 || !pathElements[1].Equals("api") || !pathElements[2].Equals("v1"))
                return true;

            string deviceType = pathElements[3].ToLowerInvariant();
            if (!int.TryParse(pathElements[4], out int deviceNumber))
                return true;
            string operation = pathElements[5].ToLowerInvariant();

            var device = DeviceManager.GetDevices().FirstOrDefault(x => x.DeviceType.ToLower().Equals(deviceType) && x.DeviceNumber == deviceNumber);
            if (device == null)
                return true;

            bool isConnected = DeviceManager.DeviceDrivers.TryGetValue((deviceType, deviceNumber), out var driver) && driver.Connected;
            if (isConnected)
                return false;
            List<string> allowedList = ["connect", "connected", "connecting", "interfaceversion", "driverversion", "driverinfo", "name"];
            ;
            return !(allowedList.Contains(operation));
        }

        /// <summary>
        /// Executes a request by performing the specified operation, building a response, and handling exceptions.
        /// </summary>
        /// <remarks>This method logs the API call, validates the request if strict Alpaca mode is
        /// enabled, and executes the provided operation. If the operation succeeds, the response is built using the
        /// provided response builder and includes the client and server transaction IDs. If an exception occurs, the
        /// method returns an appropriate error response.</remarks>
        /// <typeparam name="TResponse">The type of the response object, which must inherit from <see cref="Response"/>.</typeparam>
        /// <typeparam name="TValue">The type of the value returned by the operation.</typeparam>
        /// <param name="operation">A delegate that performs the operation and returns a value of type <typeparamref name="TValue"/>.</param>
        /// <param name="responseBuilder">A delegate that builds a response of type <typeparamref name="TResponse"/> using the value returned by
        /// <paramref name="operation"/>.</param>
        /// <param name="transactionID">The unique identifier for the server transaction.</param>
        /// <param name="clientID">A reference to the client identifier. This value may be modified during the request.</param>
        /// <param name="clientTransactionID">A reference to the client transaction identifier. This value may be modified during the request.</param>
        /// <param name="payload">An optional string containing additional data associated with the request. Defaults to an empty string.</param>
        /// <returns>An <see cref="ActionResult"/> containing the response object of type <typeparamref name="TResponse"/> if the
        /// operation succeeds, or an appropriate error response if an exception occurs.</returns>
        private ActionResult ExecuteRequest<TResponse, TValue>(
            Func<TValue> operation,
            Func<TValue, TResponse> responseBuilder,
            uint transactionID,
            ref uint clientID,
            ref uint clientTransactionID,
            string payload = "")
            where TResponse : Response, new()
        {
            try
            {
                LogAPICall(HttpContext.Connection.RemoteIpAddress, HttpContext.Request.Path.ToString(), clientID, clientTransactionID, transactionID, payload);

                if (DeviceManager.Configuration.RunInStrictAlpacaMode)
                {
                    if (BadRequestAlpacaProtocol(out BadRequestObjectResult result, ref clientID, ref clientTransactionID))
                    {
                        return result;
                    }
                }

                //if (DeviceManager.Configuration.RequireConnect)
                //{
                    if (DeviceCannotAcceptOperation())
                    {
                        return Ok(ResponseHelpers.ExceptionResponseBuilder<TResponse>(new NotConnectedException(),
                            clientTransactionID, transactionID));
                    }
                //}

                TValue value = operation.Invoke();
                TResponse response = responseBuilder(value);
                response.ClientTransactionID = clientTransactionID;
                response.ServerTransactionID = transactionID;
                return Ok(response);
            }
            catch (DeviceNotFoundException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                return Ok(ResponseHelpers.ExceptionResponseBuilder<TResponse>(ex, clientTransactionID, transactionID));
            }
        }

        /// <summary>
        /// Processes a client request by executing the specified operation and returning a response.
        /// </summary>
        /// <remarks>This method is intended for internal use and is not exposed through the API, as
        /// indicated by the <see cref="ApiExplorerSettingsAttribute"/> with <c>IgnoreApi = true</c>.</remarks>
        /// <param name="operation">A delegate representing the operation to execute. The operation must return a <see langword="true"/> or <see
        /// langword="false"/> value indicating success or failure.</param>
        /// <param name="transactionID">The unique identifier for the transaction. This value is used to track the request.</param>
        /// <param name="clientID">The unique identifier for the client making the request. This value is updated during the request
        /// processing.</param>
        /// <param name="clientTransactionID">The unique identifier for the client's transaction. This value is updated during the request processing.</param>
        /// <param name="payload">An optional string containing additional data or parameters for the request. Defaults to an empty string if
        /// not provided.</param>
        /// <returns>An <see cref="ActionResult{T}"/> containing a <see cref="BoolResponse"/> object that indicates the result of
        /// the operation. The <see cref="BoolResponse.Value"/> property will be <see langword="true"/> if the operation
        /// succeeded; otherwise, <see langword="false"/>.</returns>
        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<BoolResponse> ProcessRequest(Func<bool> operation, uint transactionID, uint clientID, uint clientTransactionID, string payload = "")
        {
            return ExecuteRequest<BoolResponse, bool>(
                operation,
                value => new BoolResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<DeviceStateResponse> ProcessRequest(Func<List<StateValue>> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<DeviceStateResponse, List<StateValue>>(
                operation,
                value => new DeviceStateResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<DriveRatesResponse> ProcessRequest(Func<ITrackingRates> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<DriveRatesResponse, IList<DriveRate>>(
                () => {
                    var rates = operation.Invoke();
                    IList<DriveRate> res = new List<DriveRate>();
                    foreach (DriveRate rate in rates)
                        res.Add(rate);
                    return res;
                },
                value => new DriveRatesResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<DoubleResponse> ProcessRequest(Func<double> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<DoubleResponse, double>(
                operation,
                value => new DoubleResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<AxisRatesResponse> ProcessRequest(Func<IAxisRates> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<AxisRatesResponse, IList<AxisRate>>(
                () => {
                    var rates = operation.Invoke();
                    IList<AxisRate> res = new List<AxisRate>();
                    foreach (IRate rate in rates)
                        res.Add(new AxisRate(rate.Minimum, rate.Maximum));
                    return res;
                },
                value => new AxisRatesResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<IntArray2DResponse> ProcessRequest(Func<int[,]> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<IntArray2DResponse, int[,]>(
                operation,
                value => new IntArray2DResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<StringListResponse> ProcessRequest(Func<IList<string>> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<StringListResponse, IList<string>>(
                operation,
                value => new StringListResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<IntListResponse> ProcessRequest(Func<IList<int>> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<IntListResponse, IList<int>>(
                operation,
                value => new IntListResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<IntResponse> ProcessRequest(Func<int> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<IntResponse, int>(
                operation,
                value => new IntResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<StringResponse> ProcessRequest(Func<string> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<StringResponse, string>(
                operation,
                value => new StringResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<Response> ProcessRequest(Action operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<Response, bool>(
                () => { operation.Invoke(); return true; },
                _ => new Response(),
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        public ActionResult<DeviceStateResponse> ProcessRequest(Func<IList<IStateValue>> operation, uint transactionID, uint clientID = 0, uint clientTransactionID = 0, string payload = "")
        {
            return ExecuteRequest<DeviceStateResponse, List<StateValue>>(
                () => {
                    IList<IStateValue> stateValues = operation.Invoke();
                    List<StateValue> response = new();
                    foreach (var stateValue in stateValues)
                        response.Add(new StateValue(stateValue.Name, stateValue.Value));
                    return response;
                },
                value => new DeviceStateResponse { Value = value },
                transactionID,
                ref clientID,
                ref clientTransactionID,
                payload
            );
        }

        /// <summary>
        /// Log out an API request to the ASCOM Standard Logger Instance. This logs at a level of Verbose.
        /// </summary>
        /// <param name="remoteIpAddress">The IP Address of the remote computer</param>
        /// <param name="request">The requested API</param>
        /// <param name="clientID">The Client ID</param>
        /// <param name="clientTransactionID">The Client Transaction ID</param>
        /// <param name="transactionID">The Server Transaction ID</param>
        /// <param name="payload">The function payload if any exists</param>
        private static void LogAPICall(IPAddress remoteIpAddress, string request, uint clientID, uint clientTransactionID, uint transactionID, string payload = "")
        {
            if (payload == null || payload == string.Empty)
            {
                Logging.LogVerbose($"Transaction: {transactionID} - {remoteIpAddress} ({clientID}, {clientTransactionID}) requested {request}");
            }
            else
            {
                Logging.LogVerbose($"Transaction: {transactionID} - {remoteIpAddress} ({clientID}, {clientTransactionID}) requested {request} with payload {payload}");
            }
        }
    }
}