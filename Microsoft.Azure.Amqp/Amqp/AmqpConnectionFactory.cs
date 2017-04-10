﻿//----------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//----------------------------------------------------------------

namespace Microsoft.Azure.Amqp
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.Azure.Amqp.Sasl;
    using Microsoft.Azure.Amqp.Transport;

    public class AmqpConnectionFactory
    {
        public Task<AmqpConnection> OpenConnectionAsync(string address)
        {
            return this.OpenConnectionAsync(address, AmqpConstants.DefaultTimeout);
        }

        public Task<AmqpConnection> OpenConnectionAsync(string address, TimeSpan timeout)
        {
            return this.OpenConnectionAsync(new Uri(address), timeout);
        }

        public Task<AmqpConnection> OpenConnectionAsync(Uri addressUri, TimeSpan timeout)
        {
            SaslHandler saslHandler = null;

            if (!string.IsNullOrEmpty(addressUri.UserInfo))
            {
                string[] parts = addressUri.UserInfo.Split(':');
                if (parts.Length > 2)
                {
                    throw new ArgumentException("addressUri.UserInfo " + addressUri.UserInfo);
                }

                string userName = Uri.UnescapeDataString(parts[0]);
                string password = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

#if !PCL
                saslHandler = new SaslPlainHandler() { AuthenticationIdentity = userName, Password = password };
#endif
            }

            return OpenConnectionAsync(addressUri, saslHandler, timeout);
        }

        public async Task<AmqpConnection> OpenConnectionAsync(Uri addressUri, SaslHandler saslHandler, TimeSpan timeout)
        {
            bool isSsl;
            if (addressUri.Scheme.Equals(AmqpConstants.SchemeAmqp, StringComparison.OrdinalIgnoreCase))
            {
                isSsl = false;
            }
            else if (addressUri.Scheme.Equals(AmqpConstants.SchemeAmqp, StringComparison.OrdinalIgnoreCase))
            {
                isSsl = true;
            }
            else
            {
                throw new NotSupportedException(addressUri.Scheme);
            }

            TransportSettings transportSettings;
            TcpTransportSettings tcpSettings = new TcpTransportSettings()
            {
                Host = addressUri.Host,
                Port = addressUri.Port > -1 ? addressUri.Port : (isSsl ? AmqpConstants.DefaultSecurePort : AmqpConstants.DefaultPort)
            };

            if (isSsl)
            {
                TlsTransportSettings tlsSettings = new TlsTransportSettings(tcpSettings);
                tlsSettings.TargetHost = addressUri.Host;
                transportSettings = tlsSettings;
            }
            else
            {
                transportSettings = tcpSettings;
            }

            AmqpSettings settings = new AmqpSettings();

            if (saslHandler != null)
            {
                // Provider for "AMQP3100"
                SaslTransportProvider saslProvider = new SaslTransportProvider();
                saslProvider.Versions.Add(new AmqpVersion(1, 0, 0));
                saslProvider.AddHandler(saslHandler);
                settings.TransportProviders.Add(saslProvider);
            }

            // Provider for "AMQP0100"
            AmqpTransportProvider amqpProvider = new AmqpTransportProvider();
            amqpProvider.Versions.Add(new AmqpVersion(new Version(1, 0, 0, 0)));
            settings.TransportProviders.Add(amqpProvider);

            AmqpTransportInitiator initiator = new AmqpTransportInitiator(settings, transportSettings);
            TransportBase transport = await Task.Factory.FromAsync(
                (c, s) => initiator.BeginConnect(timeout, c, s),
                (r) => initiator.EndConnect(r),
                null);

            try
            {
                AmqpConnectionSettings connectionSettings = new AmqpConnectionSettings()
                {
                    ContainerId = Guid.NewGuid().ToString(),
                    HostName = addressUri.Host
                };

                AmqpConnection connection = new AmqpConnection(transport, settings, connectionSettings);
                await connection.OpenAsync(timeout);

                return connection;
            }
            catch
            {
                transport.Abort();
                throw;
            }
        }
    }
}
