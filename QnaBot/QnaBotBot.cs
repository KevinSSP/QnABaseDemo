// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
// LUIS Tutorial - add dependencies
using Microsoft.ApplicationInsights;

namespace QnaBot
{
    /// <summary>
    /// Represents a bot that processes incoming activities.
    /// For each user interaction, an instance of this class is created and the OnTurnAsync method is called.
    /// This is a Transient lifetime service.  Transient lifetime services are created
    /// each time they're requested. For each Activity received, a new instance of this
    /// class is created. Objects that are expensive to construct, or have a lifetime
    /// beyond the single turn, should be carefully managed.
    /// For example, the <see cref="MemoryStorage"/> object and associated
    /// <see cref="IStatePropertyAccessor{T}"/> object are created with a singleton lifetime.
    /// </summary>
    /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.1"/>
    public class QnaBotBot : IBot
    {
        //private readonly QnaBotAccessors _accessors;
        private readonly QnAMaker _qnaMaker;
        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the class.
        /// </summary>
        /// /// <param name="qnaMaker">The service to connect to th QnA Azure service</param>
        /// <param name="loggerFactory">A <see cref="ILoggerFactory"/> that is hooked to the Azure App Service provider.</param>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-2.1#windows-eventlog-provider"/>
        public QnaBotBot(QnAMaker qnaMaker, ILoggerFactory loggerFactory)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _logger = loggerFactory.CreateLogger<QnaBotBot>();
            _logger.LogTrace("Turn start.");
            //_accessors = accessors ?? throw new System.ArgumentNullException(nameof(accessors));
            _qnaMaker = qnaMaker;
        }

        /// <summary>
        /// Every conversation turn for our Echo Bot will call this method.
        /// There are no dialogs used, since it's "single turn" processing, meaning a single
        /// request and response.
        /// </summary>
        /// <param name="turnContext">A <see cref="ITurnContext"/> containing all the data needed
        /// for processing this conversation turn. </param>
        /// <param name="cancellationToken">(Optional) A <see cref="CancellationToken"/> that can be used by other objects
        /// or threads to receive notice of cancellation.</param>
        /// <returns>A <see cref="Task"/> that represents the work queued to execute.</returns>
        /// <seealso cref="BotStateSet"/>
        /// <seealso cref="ConversationState"/>
        /// <seealso cref="IMiddleware"/>
        public async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (turnContext.Activity.Type == ActivityTypes.Message)
            {
                if (string.IsNullOrWhiteSpace(turnContext.Activity.Text))
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text("Esto no funciona a menos que digas algo primero."), cancellationToken);
                    return;
                }

                var results = await _qnaMaker.GetAnswersAsync(turnContext).ConfigureAwait(false);

                if (results.Any())
                {
                    // Aqui va la respuesta que viene de QnA
                    LogToApplicationInsights(results, turnContext.Activity.Text.ToString());
                    var topResult = results.First();
                    await turnContext.SendActivityAsync(MessageFactory.Text(topResult.Answer), cancellationToken);

                }
                else
                {
                    // Send to Application Insights
                    await turnContext.SendActivityAsync(MessageFactory.Text("Lo siento, no te entiendo."), cancellationToken);

                }
            }
            else
            {
                await turnContext.SendActivityAsync($"{turnContext.Activity.Type} evento detectado");
            }
        }

        // QnA AppInsights
        public void LogToApplicationInsights(QueryResult[] results, String texto)
        {
            // Create Application Insights object
            TelemetryClient telemetry = new TelemetryClient();

            // Set Application Insights Instrumentation Key from App Settings
            telemetry.Context.InstrumentationKey = "71265168-782c-4938-83b5-b9ff817b2aba";
            //ConfigurationManager.AppSettings["BotDevAppInsightsKey"];
            var topResult = results.First();
            // Collect information to send to Application Insights
            Dictionary<string, string> logProperties = new Dictionary<string, string>();
            logProperties.Add("QnA_query", texto);
            logProperties.Add("QnA_ScoringQuery", topResult.Score.ToString());
            logProperties.Add("QnA_Question", topResult.Questions[0].ToString());
            logProperties.Add("QnA_Answer", topResult.Answer);

            // Send to Application Insights
            telemetry.TrackTrace("QnA", Microsoft.ApplicationInsights.DataContracts.SeverityLevel.Information, logProperties);
        }
    }
}
