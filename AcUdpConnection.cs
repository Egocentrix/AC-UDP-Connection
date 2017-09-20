using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.IO;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;

namespace AcUdpCommunication
{
    public class AcUdpConnection
    {
        private const int AC_PORT = 9996;

        public ConnectionType dataType { get; private set; } // The type of data to request from the server.
        public SessionInfo sessionInfo { get; private set; } = new SessionInfo();
        public LapInfo lapInfo { get; private set; } = new LapInfo();
        public CarInfo carInfo { get; private set; } = new CarInfo();
        
        public bool isConnected { get; private set; }

        private HostName AcHost;
        private DatagramSocket socket;
        private DataWriter writer;

        public delegate void UpdatedEventDelegate(object sender, AcUpdateEventArgs e);
        public event UpdatedEventDelegate LapUpdate;
        public event UpdatedEventDelegate CarUpdate;

        public AcUdpConnection(string IpAddress, ConnectionType mode)
        {
            dataType = mode;
            AcHost = new HostName(IpAddress);
            
        }

        ~AcUdpConnection()
        {
            Disconnect();

        }

        public async void Connect()
        {
            if (isConnected) return;
            try
            {
                socket = new DatagramSocket();
                // Connect to the server, save the datawriter for later and send initial handshake;
                await socket.ConnectAsync(AcHost, AC_PORT.ToString());
                writer = new DataWriter(socket.OutputStream);

                socket.MessageReceived += Socket_MessageReceived;
                sendHandshake(AcConverter.handshaker.HandshakeOperation.Connect);

            }
            catch (Exception)
            {
                throw;
            }
        }

        public void Disconnect()
        {
            // Sign off from server, then close the socket.
            socket.MessageReceived -= Socket_MessageReceived;
            sendHandshake(AcConverter.handshaker.HandshakeOperation.Disconnect);
            socket.Dispose();
            isConnected = false;
        }

        private async void sendHandshake(AcConverter.handshaker.HandshakeOperation operationId)
        {
            // Calculate handshake bytes and send them.
            byte[] sendbytes = AcConverter.structToBytes(new AcConverter.handshaker(operationId));
            writer.WriteBytes(sendbytes);
            await writer.StoreAsync();
        }

        private void Socket_MessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            DataReader reader = args.GetDataReader();
            byte[] receivebytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(receivebytes);

            if (!isConnected) // Received data is handshake response.
            {
                // Check if it is a Handshake response-packet.
                System.Diagnostics.Debug.Assert(receivebytes.Length == Marshal.SizeOf<AcConverter.handshakerResponse>());

                AcConverter.handshakerResponse response = AcConverter.bytesToStruct<AcConverter.handshakerResponse>(receivebytes);

                // Set session info data.
                sessionInfo.driverName = AcHelperFunctions.SanitiseString(response.driverName);
                sessionInfo.carName = AcHelperFunctions.SanitiseString(response.carName);
                sessionInfo.trackName = AcHelperFunctions.SanitiseString(response.trackName);
                sessionInfo.trackLayout = AcHelperFunctions.SanitiseString(response.trackConfig);

                // Confirm handshake with data type.
                sendHandshake((AcConverter.handshaker.HandshakeOperation)dataType);
                isConnected = true;
            }
            else // An actual info packet!
            {
                switch (dataType)
                {
                    case ConnectionType.CarInfo:
                        System.Diagnostics.Debug.Assert(receivebytes.Length == Marshal.SizeOf<AcConverter.RTCarInfo>());
                        AcConverter.RTCarInfo rtcar = AcConverter.bytesToStruct<AcConverter.RTCarInfo>(receivebytes);

                        carInfo.speedAsKmh = rtcar.speed_Kmh;
                        carInfo.engineRPM = rtcar.engineRPM;
                        carInfo.Gear = rtcar.gear;

                        carInfo.currentLapTime = TimeSpan.FromMilliseconds(rtcar.lapTime);
                        carInfo.lastLapTime = TimeSpan.FromMilliseconds(rtcar.lastLap);
                        carInfo.bestLapTime = TimeSpan.FromMilliseconds(rtcar.bestLap);

                        if (CarUpdate != null)
                        {
                            AcUpdateEventArgs updateArgs = new AcUpdateEventArgs();
                            updateArgs.carInfo = this.carInfo;

                            CarUpdate(this, updateArgs);
                        }
                        break;
                    case ConnectionType.LapTime:
                        // Check if it is the right packet.
                        System.Diagnostics.Debug.Assert(receivebytes.Length == Marshal.SizeOf<AcConverter.RTLap>());

                        AcConverter.RTLap rtlap = AcConverter.bytesToStruct<AcConverter.RTLap>(receivebytes);

                        // Set last lap info data.
                        lapInfo.carName = AcHelperFunctions.SanitiseString(rtlap.carName);
                        lapInfo.driverName = AcHelperFunctions.SanitiseString(rtlap.driverName);
                        lapInfo.carNumber = rtlap.carIdentifierNumber;
                        lapInfo.lapNumber = rtlap.lap;
                        lapInfo.lapTime = TimeSpan.FromMilliseconds(rtlap.time);

                        if (LapUpdate != null)
                        {
                            AcUpdateEventArgs updateArgs = new AcUpdateEventArgs();
                            updateArgs.lapInfo = this.lapInfo;

                            LapUpdate(this, updateArgs);
                        }   
                        break;
                    default:
                        break;
                }
            }
            
        }

        
        public enum ConnectionType
        {
            CarInfo = 1,
            LapTime = 2
        };

        public class AcUpdateEventArgs : EventArgs
        {
            public LapInfo lapInfo;
            public CarInfo carInfo;
        }
    }

}
