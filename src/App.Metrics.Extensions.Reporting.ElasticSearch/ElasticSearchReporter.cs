﻿// Copyright (c) Allan Hardy. All rights reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using App.Metrics.Abstractions.Reporting;
using App.Metrics.Apdex;
using App.Metrics.Core.Abstractions;
using App.Metrics.Counter;
using App.Metrics.Extensions.Reporting.ElasticSearch.Client;
using App.Metrics.Extensions.Reporting.ElasticSearch.Extensions;
using App.Metrics.Health;
using App.Metrics.Histogram;
using App.Metrics.Infrastructure;
using App.Metrics.Meter;
using App.Metrics.Tagging;
using App.Metrics.Timer;
using Microsoft.Extensions.Logging;

namespace App.Metrics.Extensions.Reporting.ElasticSearch
{
    public class ElasticSearchReporter : IMetricReporter
    {
        private readonly ElasticSearchBulkClient _client;
        private readonly ILogger<ElasticSearchReporter> _logger;
        private readonly Func<string, string, string> _metricNameFormatter;
        private readonly BulkPayloadBuilder _payloadBuilder;
        private bool _disposed;

        public ElasticSearchReporter(
            ElasticSearchBulkClient client,
            BulkPayloadBuilder payloadBuilder,
            TimeSpan reportInterval,
            ILoggerFactory loggerFactory,
            Func<string, string, string> metricNameFormatter)
            : this(
                client,
                payloadBuilder,
                reportInterval,
                typeof(ElasticSearchReporter).Name,
                loggerFactory,
                metricNameFormatter)
        {
        }

        public ElasticSearchReporter(
            ElasticSearchBulkClient client,
            BulkPayloadBuilder payloadBuilder,
            TimeSpan reportInterval,
            string name,
            ILoggerFactory loggerFactory,
            Func<string, string, string> metricNameFormatter)
        {
            ReportInterval = reportInterval;
            Name = name;

            _payloadBuilder = payloadBuilder;
            _metricNameFormatter = metricNameFormatter;
            _logger = loggerFactory.CreateLogger<ElasticSearchReporter>();
            _client = client;
        }

        public string Name { get; }

        public TimeSpan ReportInterval { get; }

        public void Dispose() { Dispose(true); }

        public void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Free any other managed objects here.
                    _payloadBuilder.Clear();
                }
            }

            _disposed = true;
        }

        public async Task<bool> EndAndFlushReportRunAsync(IMetrics metrics)
        {
            _logger.LogTrace($"Ending {Name} Run");

            var result = await _client.WriteAsync(_payloadBuilder.Payload);

            _payloadBuilder.Clear();

            return result;
        }

        public void ReportEnvironment(EnvironmentInfo environmentInfo) { }

        public void ReportHealth(
            GlobalMetricTags globalTags,
            IEnumerable<HealthCheck.Result> healthyChecks,
            IEnumerable<HealthCheck.Result> degradedChecks,
            IEnumerable<HealthCheck.Result> unhealthyChecks)
        {
            // Health checks are reported as metrics as well
        }

        public void ReportMetric<T>(string context, MetricValueSourceBase<T> valueSource)
        {
            _logger.LogTrace($"Packing Metric {typeof(T)} for {Name}");

            if (typeof(T) == typeof(double))
            {
                ReportGauge(context, valueSource as MetricValueSourceBase<double>);
            }
            else if (typeof(T) == typeof(CounterValue))
            {
                ReportCounter(context, valueSource as MetricValueSourceBase<CounterValue>);
            }
            else if (typeof(T) == typeof(MeterValue))
            {
                ReportMeter(context, valueSource as MetricValueSourceBase<MeterValue>);
            }
            else if (typeof(T) == typeof(TimerValue))
            {
                ReportTimer(context, valueSource as MetricValueSourceBase<TimerValue>);
            }
            else if (typeof(T) == typeof(HistogramValue))
            {
                ReportHistogram(context, valueSource as MetricValueSourceBase<HistogramValue>);
            }
            else if (typeof(T) == typeof(ApdexValue))
            {
                ReportApdex(context, valueSource as MetricValueSourceBase<ApdexValue>);
            }

            _logger.LogTrace($"Finished Packing Metric {typeof(T)} for {Name}");
        }

        public void StartReportRun(IMetrics metrics)
        {
            _logger.LogTrace($"Starting {Name} Report Run");

            _payloadBuilder.Init();
        }

        private void ReportApdex(string context, MetricValueSourceBase<ApdexValue> valueSource)
        {
            var apdexValueSource = valueSource as ApdexValueSource;

            if (apdexValueSource == null)
            {
                return;
            }

            var data = new Dictionary<string, object>();

            valueSource.Value.AddApdexValues(data);

            _payloadBuilder.PackValueSource("apdex", _metricNameFormatter, context, valueSource, data);
        }

        private void ReportCounter(string context, MetricValueSourceBase<CounterValue> valueSource)
        {
            var counterValueSource = valueSource as CounterValueSource;

            if (counterValueSource == null)
            {
                return;
            }

            if (counterValueSource.Value.Items.Any() && counterValueSource.ReportSetItems)
            {
                foreach (var item in counterValueSource.Value.Items.Distinct())
                {
                    _payloadBuilder.PackCounterSetItems("counter", _metricNameFormatter, context, valueSource, item, counterValueSource);
                }
            }

            _payloadBuilder.PackValueSource("counter", _metricNameFormatter, context, valueSource, counterValueSource);
        }

        private void ReportGauge(string context, MetricValueSourceBase<double> valueSource)
        {
            if (!double.IsNaN(valueSource.Value) && !double.IsInfinity(valueSource.Value))
            {
                _payloadBuilder.PackValueSource("gauge", _metricNameFormatter, context, valueSource);
            }
        }

        private void ReportHistogram(string context, MetricValueSourceBase<HistogramValue> valueSource)
        {
            var data = new Dictionary<string, object>();

            valueSource.Value.AddHistogramValues(data);

            _payloadBuilder.PackValueSource("histogram", _metricNameFormatter, context, valueSource, data);
        }

        private void ReportMeter(string context, MetricValueSourceBase<MeterValue> valueSource)
        {
            var data = new Dictionary<string, object>();

            if (valueSource.Value.Items.Any())
            {
                foreach (var item in valueSource.Value.Items.Distinct())
                {
                    _payloadBuilder.PackMeterSetItems("meter", _metricNameFormatter, context, valueSource, item);
                }
            }

            valueSource.Value.AddMeterValues(data);

            _payloadBuilder.PackValueSource("meter", _metricNameFormatter, context, valueSource, data);
        }

        private void ReportTimer(string context, MetricValueSourceBase<TimerValue> valueSource)
        {
            var data = new Dictionary<string, object>();

            valueSource.Value.Rate.AddMeterValues(data);
            valueSource.Value.Histogram.AddHistogramValues(data);

            _payloadBuilder.PackValueSource("timer", _metricNameFormatter, context, valueSource, data);
        }
    }
}