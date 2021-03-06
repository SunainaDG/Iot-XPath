﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.Common.Configurations;
using Microsoft.Azure.Devices.Applications.RemoteMonitoring.DeviceAdmin.Infrastructure.BusinessLogic;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Devices.Applications.RemoteMonitoring.EventProcessor.WebJob.Processors
{
    public class FeedbackProcessorFactory : IEventProcessorFactory
    {
        private readonly ILocationJerkLogic _locationJerkLogic;
        private readonly IConfigurationProvider _configurationProvider;

        private readonly ConcurrentDictionary<string, FeedbackProcessor> _eventProcessors = new ConcurrentDictionary<string, FeedbackProcessor>();
        private readonly ConcurrentQueue<FeedbackProcessor> _closedProcessors = new ConcurrentQueue<FeedbackProcessor>();

        public FeedbackProcessorFactory(
            ILocationJerkLogic locationJerkLogic,
            IConfigurationProvider configurationProvider)
        {
            _locationJerkLogic = locationJerkLogic;
            _configurationProvider = configurationProvider;
        }

        public int ActiveProcessors
        {
            get { return _eventProcessors.Count; }
        }

        public int TotalMessages
        {
            get
            {
                var amount = _eventProcessors.Select(p => p.Value.TotalMessages).Sum();
                amount += _closedProcessors.Select(p => p.TotalMessages).Sum();
                return amount;
            }
        }

        public IEventProcessor CreateEventProcessor(PartitionContext context)
        {
            var processor = new FeedbackProcessor(
                _locationJerkLogic,
                _configurationProvider);

            processor.ProcessorClosed += this.ProcessorOnProcessorClosed;
            _eventProcessors.TryAdd(context.Lease.PartitionId, processor);
            return processor;
        }

        public Task WaitForAllProcessorsInitialized(TimeSpan timeout)
        {
            return this.WaitForAllProcessorsCondition(p => p.IsInitialized, timeout);
        }

        public Task WaitForAllProcessorsClosed(TimeSpan timeout)
        {
            return this.WaitForAllProcessorsCondition(p => p.IsClosed, timeout);
        }

        public async Task WaitForAllProcessorsCondition(Func<FeedbackProcessor, bool> predicate, TimeSpan timeout)
        {
            TimeSpan sleepInterval = TimeSpan.FromSeconds(2);
            while (!_eventProcessors.Values.All(predicate))
            {
                if (timeout > sleepInterval)
                {
                    timeout = timeout.Subtract(sleepInterval);
                }
                else
                {
                    throw new TimeoutException("Condition not satisfied within expected timeout.");
                }
                await Task.Delay(sleepInterval);
            }
        }

        public void ProcessorOnProcessorClosed(object sender, EventArgs eventArgs)
        {
            var processor = sender as FeedbackProcessor;
            if (processor != null)
            {
                _eventProcessors.TryRemove(processor.Context.Lease.PartitionId, out processor);
                _closedProcessors.Enqueue(processor);
            }
        }
    }
}
