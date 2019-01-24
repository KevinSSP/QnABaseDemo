// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Builder.Integration;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Configuration;
using Microsoft.Bot.Connector.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QnaBot
{
    /// <summary>
    /// The Startup class configures services and the request pipeline.
    /// </summary>
    public class Startup
    {
        private ILoggerFactory _loggerFactory;
        private readonly bool _isProduction;

        public Startup(IHostingEnvironment env)
        {
            _isProduction = env.IsProduction();
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        /// <summary>
        /// Gets the configuration that represents a set of key/value application configuration properties.
        /// </summary>
        /// <value>
        /// The <see cref="IConfiguration"/> that represents a set of key/value application configuration properties.
        /// </value>
        public IConfiguration Configuration { get; }

        /// <summary>
        /// This method gets called by the runtime. Use this method to add services to the container.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> specifies the contract for a collection of service descriptors.</param>
        /// <seealso cref="IStatePropertyAccessor{T}"/>
        /// <seealso cref="https://docs.microsoft.com/en-us/aspnet/web-api/overview/advanced/dependency-injection"/>
        /// <seealso cref="https://docs.microsoft.com/en-us/azure/bot-service/bot-service-manage-channels?view=azure-bot-service-4.0"/>
        public void ConfigureServices(IServiceCollection services)
        {
            var secretKey = Configuration.GetSection("botFileSecret")?.Value;
            var botFilePath = Configuration.GetSection("botFilePath")?.Value;

            // Loads .bot configuration file and adds a singleton that your Bot can access through dependency injection.
            var botConfig = BotConfiguration.Load(botFilePath ?? @".\BotConfiguration.bot", secretKey);
            services.AddSingleton(sp => botConfig ?? throw new InvalidOperationException($"No se pudo cargar el archivo de configuración .bot . ({botConfig})"));

            // Retrieve current endpoint.
            var environment = _isProduction ? "production" : "development";

            ICredentialProvider credentialProvider = null;

            foreach (var service in botConfig.Services)
            {
                switch (service.Type)
                {
                    case ServiceTypes.Endpoint:
                        if (service is EndpointService endpointService)
                        {
                            credentialProvider = new SimpleCredentialProvider(endpointService.AppId, endpointService.AppPassword);
                        }

                        break;
                    case ServiceTypes.QnA:
                        if (service is QnAMakerService qnaMakerService)
                        {
                            var qnaEndpoint = new QnAMakerEndpoint
                            {
                                Host = qnaMakerService.Hostname,
                                EndpointKey = qnaMakerService.EndpointKey,
                                KnowledgeBaseId = qnaMakerService.KbId,
                            };
                            services.AddSingleton(new QnAMaker(qnaEndpoint));
                        }

                        break;
                }
            }

            services.AddBot<QnaBotBot>(options => ConfigureBot(options, credentialProvider));
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;

            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseBotFramework();
        }

        private void ConfigureBot(BotFrameworkOptions options, ICredentialProvider credentialProvider)
        {
            // Set the CredentialProvider for the bot. It uses this to authenticate with the QnA service in Azure
            options.CredentialProvider = credentialProvider
                                         ?? throw new InvalidOperationException("Falta información en el endpoint del archivo de configuración de arranque.");

            // Creates a logger for the application to use.
            ILogger logger = _loggerFactory.CreateLogger<QnaBotBot>();

            // Catches any errors that occur during a conversation turn and logs them.
            options.OnTurnError = async (context, exception) =>
            {
                logger.LogError($"Excepción atrapada : {exception}");
                await context.SendActivityAsync("Lo siento, parece que algo salió mal.");
            };

            // The Memory Storage used here is for local bot debugging only. When the bot
            // is restarted, everything stored in memory will be gone.
            IStorage dataStore = new MemoryStorage();

            // Create Conversation State object.
            // The Conversation State object is where we persist anything at the conversation-scope.
            var conversationState = new ConversationState(dataStore);

            options.State.Add(conversationState);
        }
    }
}
