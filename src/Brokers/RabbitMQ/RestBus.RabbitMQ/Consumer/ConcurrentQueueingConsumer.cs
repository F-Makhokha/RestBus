﻿using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Concurrent;
using System.IO;

namespace RestBus.RabbitMQ.Consumer
{
    /// <summary>
    /// Provides an implementation of a queuing consumer that uses a ConcurrentQueue internally.
    /// </summary>
    internal sealed class ConcurrentQueueingConsumer : IBasicConsumer
    {
        readonly ConcurrentQueue<BasicDeliverEventArgs> _queue = new ConcurrentQueue<BasicDeliverEventArgs>();
        volatile bool _isRunning = false;
        volatile bool _isClosed = false;

        /// <summary>
        /// Initializes and instance of the <see cref="ConcurrentQueueingConsumer"/>
        /// </summary>
        /// <param name="model">The AMQP model.</param>
        public ConcurrentQueueingConsumer(IModel model)
        {
            Model = model;
            ConsumerTag = null;
            ShutdownReason = null;
        }

        /// <summary>
        /// Retrieve the consumer tag this consumer is registered as; to be used when discussing this consumer
        /// with the server, for instance with <see cref="IModel.BasicCancel"/>.
        /// </summary>
        public string ConsumerTag { get; set; }

        /// <summary>
        /// Returns true while the consumer is registered and expecting deliveries from the broker.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }
            private set
            {
                _isRunning = value;
            }
        }

        /// <summary>
        /// If our <see cref="IModel"/> shuts down, this property will contain a description of the reason for the
        /// shutdown. Otherwise it will contain null. See <see cref="ShutdownEventArgs"/>.
        /// </summary>
        public ShutdownEventArgs ShutdownReason { get; private set; }

        /// <summary>
        /// Retrieve the <see cref="IModel"/> this consumer is associated with,
        ///  for use in acknowledging received messages, for instance.
        /// </summary>
        public IModel Model
        {
            get; private set;
        }

        /// <summary>
        /// Signalled when the consumer gets cancelled.
        /// </summary>
        public event EventHandler<ConsumerEventArgs> ConsumerCancelled;


        /// <summary>
        ///  Called when the consumer is cancelled for reasons other than by a basicCancel:
        ///  e.g. the queue has been deleted (either by this channel or  by any other channel).
        ///  See <see cref="HandleBasicCancelOk"/> for notification of consumer cancellation due to basicCancel
        /// </summary>
        /// <param name="consumerTag">Consumer tag this consumer is registered.</param>
        public void HandleBasicCancel(string consumerTag)
        {
            OnCancel();
        }

        /// <summary>
        /// Called upon successful deregistration of the consumer from the broker.
        /// </summary>
        /// <param name="consumerTag">Consumer tag this consumer is registered.</param>
        public void HandleBasicCancelOk(string consumerTag)
        {
            OnCancel();
        }

        /// <summary>
        /// Called upon successful registration of the consumer with the broker.
        /// </summary>
        /// <param name="consumerTag">Consumer tag this consumer is registered.</param>
        public void HandleBasicConsumeOk(string consumerTag)
        {
            ConsumerTag = consumerTag;
            IsRunning = true;
        }

        public void HandleBasicDeliver(string consumerTag, ulong deliveryTag, bool redelivered, string exchange, string routingKey, IBasicProperties properties, byte[] body)
        {
            if (_isClosed) return;

            var eventArgs = new BasicDeliverEventArgs
            {
                ConsumerTag = consumerTag,
                DeliveryTag = deliveryTag,
                Redelivered = redelivered,
                Exchange = exchange,
                RoutingKey = routingKey,
                BasicProperties = properties,
                Body = body
            };
            _queue.Enqueue(eventArgs);
        }

        /// <summary>
        /// Called each time a message arrives for this consumer.
        /// </summary>
        /// <remarks>
        /// Does nothing with the passed in information.
        /// Note that in particular, some delivered messages may require acknowledgement via <see cref="IModel.BasicAck"/>.
        /// The implementation of this method in this class does NOT acknowledge such messages.
        /// </remarks>
        public void HandleModelShutdown(object model, ShutdownEventArgs reason)
        {
            ShutdownReason = reason;
            OnCancel();
        }

        /// <summary>
        /// Called when consumer is cancelled
        /// </summary>
        private void OnCancel()
        {
            IsRunning = false;
            _isClosed = true;

            var handler = ConsumerCancelled; //copy to local variable
            if (handler != null)
            {
                handler(this, new ConsumerEventArgs(ConsumerTag));
            }

        }

        /// <summary>
        /// Attempts to dequeue an item without waiting
        /// </summary>
        /// <param name="result">The dequeued item.</param>
        /// <returns>True if succeeded, false otherwise</returns>
        public bool TryInstantDequeue(out BasicDeliverEventArgs result)
        {
            if(_isClosed)
            {
                throw new EndOfStreamException("Consumer closed");
            }
            return _queue.TryDequeue(out result);
        }
    }
}