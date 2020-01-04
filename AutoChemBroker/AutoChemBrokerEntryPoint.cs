using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using AutoChem.Core.AppInterop;
using NLog;
using uPLibrary.Networking.M2Mqtt;

namespace AutoChemBroker
{
    public class AutoChemBrokerEntryPoint : IDisposable
    {
        private readonly NameValueCollection _config;
        private List<ApplicationReference> _applicationReference = new List<ApplicationReference>();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private IApplicationConnection _app;
        private AutoChemClient _client;
        private ILiveExperimentConnection _liveExperiment;

        public AutoChemBrokerEntryPoint(NameValueCollection config)
        {
            _config = config;
            Logger.Info("Program started.");
            PopulateListOfKnownApplication();
            LoadFirstApplication();
        }


        private void PopulateListOfKnownApplication()
        {
            try
            {
                foreach (ApplicationReference applicationReference in AppReferenceXMLHelper.GetApplicationReferences())
                {
                    _applicationReference.Add(applicationReference);
                    Logger.Info($"ApplicationReference: {applicationReference.AppType.ToString()}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        private void LoadFirstApplication()
        {
            var sel = _applicationReference.First();

            try
            {
                _app = AppLocator.GetApplicationConnection(sel);
                if (_app == null)
                {
                    Logger.Error("Please start the instrument software first.");
                    Environment.Exit(-1);
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                throw;
            }

            Logger.Info($"Successfully load {sel.AppType.ToString()}");

            try
            {
                // Initializing client
                if (!CreateTcpChannel())
                {
                    Logger.Error("Failed to create tcp channel.");
                    Environment.Exit(-1);
                }

                _client = new AutoChemClient(_config);
                _app.RegisterClient(_client);
                _liveExperiment = _app.NewExperimentWithWizard("", "");
                _liveExperiment.RegisterClient(_client);
            }
            catch (Exception e)
            {
                Logger.Error(e);
                throw;
            }
        }

        public void Dispose()
        {
            if (_app != null)
            {
                _app.UnRegisterClient(_client);
                Logger.Info("Unregistered the client");
            }

            if (_liveExperiment != null)
            {
                _liveExperiment.UnRegisterClient(_client);
                Logger.Info("Unregistered from the live experiment");
            }
        }

        private bool CreateTcpChannel()
        {
            bool flag = false;
            int tcpPort = 61020;
            try
            {
                Hashtable hashtable = new Hashtable();
                hashtable[(object) "name"] = (object) ("AppInterop Test Client tcp channel " + tcpPort);
                hashtable[(object) "port"] = (object) tcpPort;
                BinaryServerFormatterSinkProvider formatterSinkProvider1 = new BinaryServerFormatterSinkProvider();
                formatterSinkProvider1.TypeFilterLevel = TypeFilterLevel.Full;
                BinaryClientFormatterSinkProvider formatterSinkProvider2 = new BinaryClientFormatterSinkProvider();
                ChannelServices.RegisterChannel(
                    (IChannel) new TcpChannel((IDictionary) hashtable,
                        (IClientChannelSinkProvider) formatterSinkProvider2,
                        (IServerChannelSinkProvider) formatterSinkProvider1), false);
                var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var str = Path.Combine(Path.GetDirectoryName(location) ?? throw new NullReferenceException(),
                    "AppInteropTestClient.exe.config");
                Logger.Info($"Configuring with {str}");
                if (File.Exists(str))
                {
                    try
                    {
                        RemotingConfiguration.Configure(str, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                        throw;
                    }
                }
                else
                {
                    Logger.Info($"Configuration not exist!");
                    Environment.Exit(-1);
                }

                flag = true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                throw;
            }

            return flag;
        }
    }
}