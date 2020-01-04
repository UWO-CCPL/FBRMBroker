using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AutoChem.Core.AppInterop;
using AutoChem.Core.Controls;
using Newtonsoft.Json;
using NLog;
using NLog.Fluent;
using NLog.LogReceiverService;
using uPLibrary.Networking.M2Mqtt;

namespace AutoChemBroker
{
    public class AutoChemClient : MarshaledForm, IApplicationClient, ILiveExperimentClient
    {
        private readonly NameValueCollection _config;
        private static Logger Logger = LogManager.GetCurrentClassLogger();
        private HttpClient _client;
        private MqttClient _mqttClient;
        private bool mqttSizeAxisTransmitted = false;


        public AutoChemClient(NameValueCollection config)
        {
            _config = config;
            _client = new HttpClient();
            var authToken = Encoding.ASCII.GetBytes($"{_config["influxdbUser"]}:{_config["influxdbPassword"]}");
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

            if (Convert.ToBoolean(_config["mqttEnabled"]))
            {
                _mqttClient = new MqttClient(_config["mqttHost"]);
                _mqttClient.Connect(_config["mqttId"]);
                Logger.Info("Connected to mqtt server");
            }
        }

        public void DocumentSelected(IApplicationConnection application, IDocumentConnection document)
        {
            Logger.Info($"Document {document.DocumentName} selected");
        }

        public void DocumentOpened(IApplicationConnection application, IDocumentConnection document)
        {
            Logger.Info($"Document {document.DocumentName} opened");
        }

        public void DocumentClosed(IApplicationConnection application, IDocumentConnection document)
        {
            Logger.Info($"Document {document.DocumentName} closed");
        }

        public void ExperimentStatusChanged(IExperimentConnection document, ExperimentStatus previousStatus,
            ExperimentStatus newStatus)
        {
            Logger.Info(
                $"Experiment {document.DocumentName} status changed from {previousStatus.ToString()} to {newStatus.ToString()}");
        }

        public void SampleAcquired(IExperimentConnection document, DateTime sampleTime)
        {
            Logger.Info($"Sample acquired from {document.DocumentName} at {sampleTime.ToShortTimeString()}");
            try
            {
                var chartConnection = document["Distributions"];
                Logger.Info($"Chart type = {chartConnection.ChartType.ToString()}");
                var chart = chartConnection as IDistributionChartConnection;
                if (chart == null) throw new ArgumentNullException(nameof(chart));

                var sampleId = chart.LoadSample(sampleTime);
                if (sampleId == Guid.Empty)
                {
                    Logger.Warn($"Unable to load sample with {sampleTime}");
                    return;
                }

                Logger.Info($"Sample Id = {sampleId}");
                Logger.Info("Got raw data:");
                var rawXMidpoints = chart.GetRawXMidpoints(sampleId);
                Logger.Info(rawXMidpoints == null ? "X=NULL" : $"Xmidpoint length = {rawXMidpoints.Length.ToString()}");
                var rawYData = chart.GetRawYData(sampleId);
                Logger.Info(rawYData == null ? "Y=NULL" : $" rawYData length = {rawYData.Length.ToString()}");


                if (Convert.ToBoolean(_config["influxdbEnabled"]))
                {
                    Logger.Info("Writing to InfluxDB");
                    var tags = new Dictionary<string, object>();
                    tags["experiment"] = document.DocumentName;
                    string lineProtocol = _config["influxdbMeasurementName"] + ",";
                    lineProtocol += $"experiment={document.DocumentName} ";
                    for (int i = 0; i < rawXMidpoints.Length; i++)
                    {
                        lineProtocol += $"{rawXMidpoints[i].ToString("0.0")}={rawYData[i].ToString("0.0")},";
                    }

                    lineProtocol = lineProtocol.TrimEnd(',');

                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

                    lineProtocol += $" {(long) (sampleTime.ToUniversalTime() - epoch).TotalMilliseconds}";

                    var databaseName = _config["influxdbDatabaseName"];

                    var task = _client.PostAsync($"{_config["influxdbUri"]}/write?db={databaseName}&precision=ms",
                        new StringContent(lineProtocol));
                    task.Wait();
                    Logger.Info(
                        $"InfluxDB response: {task.Result.StatusCode}, message={task.Result.Content.ReadAsStringAsync().Result}");
                }

                if (Convert.ToBoolean(_config["mqttEnabled"]))
                {
                    var counts = new List<double>();
                    for (int i = 0; i < rawYData.Length; i++)
                    {
                        counts.Add(rawYData[i]);
                    }

                    var countsJsonified = JsonConvert.SerializeObject(counts);

                    string payloadTopic = _config["mqttTopic"] + "/counts";
                    _mqttClient.Publish(payloadTopic, Encoding.ASCII.GetBytes(countsJsonified));
                    Logger.Info("Points written to MQTT");

                    if (!mqttSizeAxisTransmitted)
                    {
                        mqttSizeAxisTransmitted = true;
                        string sizeAxisTopic = _config["mqttTopic"] + "/sizes";
                        var sizes = new List<double>();
                        for (int i = 0; i < rawXMidpoints.Length; i++)
                        {
                            sizes.Add(rawXMidpoints[i]);
                        }

                        var sizesJsonified = JsonConvert.SerializeObject(sizes);
                        _mqttClient.Publish(sizeAxisTopic, Encoding.ASCII.GetBytes(sizesJsonified), 0, true);
                        Logger.Info("Sizes written to MQTT");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }


        public void ErrorMessageChanged(IExperimentConnection document, string errorMessage)
        {
            Logger.Warn($"Error from {document.DocumentName}: {errorMessage}");
        }

        public void WarningMessageChanged(IExperimentConnection document, string warningMessage)
        {
            Logger.Warn($"Warning from {document.DocumentName}: {warningMessage}");
        }
    }
}