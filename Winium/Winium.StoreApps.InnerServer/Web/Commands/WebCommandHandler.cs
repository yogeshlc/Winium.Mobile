﻿namespace Winium.StoreApps.InnerServer.Web.Commands
{
    #region

    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;

    using Windows.UI.Core;

    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    using Winium.StoreApps.Common;
    using Winium.StoreApps.Common.Exceptions;
    using Winium.StoreApps.InnerServer.Commands;

    #endregion

    internal abstract class WebCommandHandler : ICommandBase
    {
        private TimeSpan atomExecutionTimeout = TimeSpan.FromMilliseconds(-1);

        public CommandEnvironment Context { get; set; }

        public string Atom { get; set; }

        public string Do()
        {
            if (this.Automator == null)
            {
                throw new InvalidOperationException("Automator must be set before Do() is called");
            }

            var response = string.Empty;
            try
            {
                response = this.DoImpl();
            }
            catch (AutomationException exception)
            {
                response = this.JsonResponse(exception.Status, exception);
            }
            catch (Exception exception)
            {
                response = this.JsonResponse(ResponseStatus.UnknownError, exception);
            }

            return response;
        }

        public Automator Automator { get; set; }

        public IDictionary<string, JToken> Parameters { get; set; }

        public string Session { get; set; }

        private static string CreateArgumentString(IEnumerable<object> args)
        {
            var builder = new StringBuilder();
            foreach (var arg in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(",");
                }

                var argAsString = JsonConvert.SerializeObject(arg);
                builder.Append(argAsString);
            }

            return builder.ToString();
        }

        protected string EvaluateAtom(CommandEnvironment environment, string executedAtom, params object[] args)
        {
            var browser = environment.Browser;
            var argumentString = CreateArgumentString(args);

            var script = "(" + executedAtom + ")(" + argumentString + ");";

            string result = null;

            var sync = new ManualResetEvent(false);

            environment.Browser.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal,
                () =>
                    {
                        var op = browser.InvokeScriptAsync("eval", new[] { script });
                        op.Completed = (info, status) =>
                            {
                                try
                                {
                                    result = op.GetResults();
                                }
                                finally
                                {
                                    sync.Set();
                                }
                            };
                    });

            if (!sync.WaitOne(this.atomExecutionTimeout))
            {
                throw new TimeoutException();
            }

            return result;

            // TODO why https://github.com/forcedotcom/windowsphonedriver used to separate invokes?
            //var script = "window.top.__wd_fn_result = (" + executedAtom + ")(" + argumentString + ");";
            //var result = string.Empty;
            //var synchronizer = new ManualResetEvent(false);
            //browser.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, 
            //    () =>
            //    {
            //        try
            //        {
            //            browser.InvokeScriptAsync("eval", new []{script}).AsTask().Wait();
            //            result = browser.InvokeScriptAsync("eval", new []{"window.top.__wd_fn_result"}).AsTask().Result;
            //        }
            //        catch (Exception ex)
            //        {
            //            throw new AutomationException(string.Format(
            //                           CultureInfo.InvariantCulture,
            //                           "Unexpected exception {0}: {1}",
            //                           ex.GetType(),
            //                           ex.Message), ResponseStatus.UnknownError);
            //        }
            //        finally
            //        {
            //            synchronizer.Set();
            //        }
            //    }).AsTask().Wait(this.atomExecutionTimeout);

            //return result;
        }

        /// <summary>
        /// Sets the timeout for execution of an atom.
        /// </summary>
        /// <param name="timeout">The <see cref="TimeSpan"/> representing the timeout for the atom execution.</param>
        protected void SetAtomExecutionTimeout(TimeSpan timeout)
        {
            this.atomExecutionTimeout = timeout;
        }

        protected abstract string DoImpl();

        protected string JsonResponse(ResponseStatus status = ResponseStatus.Success, object value = null)
        {
            if (status != ResponseStatus.Success && value == null)
            {
                value = string.Format("WebDriverException {0}", Enum.GetName(typeof(ResponseStatus), status));
            }

            return JsonConvert.SerializeObject(new JsonResponse(this.Session, status, value), Formatting.Indented);
        }
    }
}
