using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ViscaOverIpSimulator
{
    class ViscaOverIpSimulator
    {
        // Camera state variables
        private static int panPosition = 0; // Pan position (range: -100 to +100)
        private static int tiltPosition = 0; // Tilt position (range: -100 to +100)
        private static int zoomLevel = 100; // Zoom level (range: 100 to 300)
        private static int focusPosition = 100; // Example range: 100 to 300
        private static bool isAutoFocus = true; // true = Auto-focus, false = Manual-focus
        private static bool isSpecialModeEnabled = false;

        static async Task Main(string[] args)
        {
            const int port = 52381; // Default VISCA over IP port

            Console.WriteLine($"Starting VISCA over IP Camera Simulator on port {port}...");
            Console.WriteLine("Listening for UDP and TCP connections...");

            // Start UDP and TCP listeners
            var udpTask = Task.Run(() => StartUdpListener(port));
            var tcpTask = Task.Run(() => StartTcpListener(port));

            await Task.WhenAll(udpTask, tcpTask);
        }

        static void StartUdpListener(int port)
        {
            UdpClient udpServer = new UdpClient(port);
            Console.WriteLine("UDP listener started.");

            while (true)
            {
                try
                {
                    IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedBytes = udpServer.Receive(ref remoteEndPoint);
                    HandleIncomingCommand(receivedBytes, remoteEndPoint, udpServer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"UDP Error: {ex.Message}");
                }
            }
        }

        static void StartTcpListener(int port)
        {
            TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            Console.WriteLine("TCP listener started.");

            while (true)
            {
                try
                {
                    var client = tcpListener.AcceptTcpClient();
                    Console.WriteLine($"TCP connection established with {client.Client.RemoteEndPoint}");

                    Task.Run(() =>
                    {
                        HandleTcpClient(client);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TCP Error: {ex.Message}");
                }
            }
        }

        static void HandleTcpClient(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[1024];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    byte[] receivedBytes = new byte[bytesRead];
                    Array.Copy(buffer, receivedBytes, bytesRead);

                    Console.WriteLine($"\nTCP Received: {BitConverter.ToString(receivedBytes).Replace("-", " ")}");

                    byte[] response = HandleViscaCommand(receivedBytes);
                    if (response != null)
                    {
                        stream.Write(response, 0, response.Length);
                        Console.WriteLine($"TCP Response: {BitConverter.ToString(response).Replace("-", " ")}");
                    }
                }
            }
        }

        static void HandleIncomingCommand(byte[] command, IPEndPoint remoteEndPoint, UdpClient udpServer)
        {
            string receivedHex = BitConverter.ToString(command).Replace("-", " ");
            Console.WriteLine($"\nUDP Received from {remoteEndPoint}: {receivedHex}");

            byte[] response = HandleViscaCommand(command);
            if (response != null)
            {
                udpServer.Send(response, response.Length, remoteEndPoint);
                string responseHex = BitConverter.ToString(response).Replace("-", " ");
                Console.WriteLine($"UDP Response to {remoteEndPoint}: {responseHex}");
            }
        }

        static byte[] HandleViscaCommand(byte[] command)
        {
            // VISCA commands processing
            if (MatchCommand(command, new byte[] { 0x81, 0x01, 0x04, 0x00, 0x02, 0xFF }))
            {
                Console.WriteLine("Command: Power On");
                return new byte[] { 0x90, 0x50, 0xFF }; // Acknowledge response
            }

            if (MatchCommand(command, new byte[] { 0x81, 0x01, 0x04, 0x00, 0x03, 0xFF }))
            {
                Console.WriteLine("Command: Power Off");
                return new byte[] { 0x90, 0x50, 0xFF }; // Acknowledge response
            }

            if (command.Length == 9 && command[0] == 0x81 && command[1] == 0x01 && command[2] == 0x06 && command[3] == 0x01)
            {
                int panSpeed = command[4];
                int tiltSpeed = command[5];
                int panDirection = command[6] == 0x01 ? -1 : (command[6] == 0x02 ? 1 : 0);
                int tiltDirection = command[7] == 0x01 ? 1 : (command[7] == 0x02 ? -1 : 0);

                panPosition += panSpeed * panDirection;
                tiltPosition += tiltSpeed * tiltDirection;
                panPosition = Math.Clamp(panPosition, -100, 100);
                tiltPosition = Math.Clamp(tiltPosition, -100, 100);

                Console.WriteLine($"Command: PTZ Move - Pan: {panPosition}, Tilt: {tiltPosition}");
                return new byte[] { 0x90, 0x50, 0xFF };
            }

            if (command.Length == 6 && command[0] == 0x81 && command[1] == 0x01 && command[2] == 0x04 && command[3] == 0x07)
            {
                int zoomDirection = command[4] == 0x27 ? 1 : (command[4] == 0x37 ? -1 : 0);
                zoomLevel += zoomDirection * 10;
                zoomLevel = Math.Clamp(zoomLevel, 100, 300);

                Console.WriteLine($"Command: Zoom - Level: {zoomLevel}");
                return new byte[] { 0x90, 0x50, 0xFF };
            }

            if (MatchCommand(command, new byte[] { 0x81, 0x09, 0x06, 0x12, 0xFF }))
            {
                Console.WriteLine("Command: Inquiry Pan/Tilt/Zoom Position");

                // Convert the pan, tilt, and zoom positions to VISCA response format
                byte[] panBytes = ConvertPositionToViscaFormat(panPosition);
                byte[] tiltBytes = ConvertPositionToViscaFormat(tiltPosition);
                byte[] zoomBytes = ConvertPositionToViscaFormat(zoomLevel);

                byte[] response = new byte[]
                {
                    0x90, // Address set
                    0x50, // Command completion
                    panBytes[0], panBytes[1], panBytes[2], panBytes[3],
                    tiltBytes[0], tiltBytes[1], tiltBytes[2], tiltBytes[3],
                    zoomBytes[0], zoomBytes[1], zoomBytes[2], zoomBytes[3],
                    0xFF // End of message
                };

                Console.WriteLine($"Response: Pan: {panPosition}, Tilt: {tiltPosition}, Zoom: {zoomLevel}");
                return response;
            }

            if (MatchCommand(command, new byte[] { 0x81, 0x09, 0x04, 0x47, 0xFF })) 
            {
                Console.WriteLine("Command: Inquiry Zoom Position");

                // Convert the zoom level to VISCA response format
                byte[] zoomBytes = ConvertPositionToViscaFormat(zoomLevel);

                byte[] response = new byte[]
                {
                    0x90, // Address set
                    0x50, // Command completion
                    zoomBytes[0], zoomBytes[1], zoomBytes[2], zoomBytes[3],
                    0xFF // End of message
                };

                Console.WriteLine($"Response: Zoom: {zoomLevel}");
                return response;
            }

            if (command.Length == 8 && command[0] == 0x81 && command[1] == 0x01 && command[2] == 0x04 && command[3] == 0x48)
            {
                // Extract the focus position from the command
                int newFocusPosition = ConvertViscaBytesToPosition(command[4], command[5]);

                // Update the focus position
                focusPosition = newFocusPosition;

                Console.WriteLine($"Command: Set Focus Position to {focusPosition}");

                // Acknowledge response
                return new byte[] { 0x90, 0x50, 0xFF };
            }

            if (MatchCommand(command, new byte[] { 0x81, 0x09, 0x04, 0x48, 0xFF })) // GetFocus command
            {
                Console.WriteLine("Command: Inquiry Focus Position");

                // Convert the focus position to VISCA response format
                byte[] focusBytes = ConvertPositionToViscaFormat(focusPosition);

                byte[] response = new byte[]
                {
                    0x90, // Address set
                    0x50, // Command completion
                    focusBytes[0], focusBytes[1], focusBytes[2], focusBytes[3],
                    0xFF // End of message
                };

                Console.WriteLine($"Response: Focus: {focusPosition}");
                return response;
            }

            // Handle SetFocusMode (Manual)
            if (MatchCommand(command, new byte[] { 0x81, 0x01, 0x04, 0x38, 0x03, 0xFF }))
            {
                isAutoFocus = false;
                Console.WriteLine("Command: Set Focus Mode - Manual");
                return new byte[] { 0x90, 0x50, 0xFF }; // Acknowledge response
            }

            // Handle SetFocusMode (Auto)
            if (MatchCommand(command, new byte[] { 0x81, 0x01, 0x04, 0x38, 0x02, 0xFF }))
            {
                isAutoFocus = true;
                Console.WriteLine("Command: Set Focus Mode - Auto");
                return new byte[] { 0x90, 0x50, 0xFF }; // Acknowledge response
            }
            // Handle GetFocusMode
            if (MatchCommand(command, new byte[] { 0x81, 0x09, 0x04, 0x38, 0xFF }))
            {
                Console.WriteLine("Command: Get Focus Mode");

                // Respond with current focus mode
                byte[] response = new byte[]
                {
                    0x90,  // Address set
                    0x50,  // Command completion
                    isAutoFocus ? (byte)0x02 : (byte)0x03, // 0x02 for Auto, 0x03 for Manual
                    0xFF   // End of message
                };

                Console.WriteLine($"Response: Focus Mode - {(isAutoFocus ? "Auto" : "Manual")}");
                return response;
            }

            // Handle absolute PTZ move command
            if (MatchCommand(command.Take(4).ToArray(), new byte[] { 0x81, 0x01, 0x06, 0x02 }))
            {
                // Extract speeds and positions
                int panSpeed = command[4];
                int tiltSpeed = command[5];

                int panPosition = ConvertViscaBytesToPosition(command[6], command[7], command[8], command[9]);
                int tiltPosition = ConvertViscaBytesToPosition(command[10], command[11], command[12], command[13]);

                // Log the received values
                Console.WriteLine($"Command: PTZ Absolute Move");
                Console.WriteLine($"Pan Speed: {panSpeed}, Tilt Speed: {tiltSpeed}");
                Console.WriteLine($"Pan Position: {panPosition}, Tilt Position: {tiltPosition}");

                // Simulate moving the camera
                MoveToPosition(panPosition, tiltPosition, panSpeed, tiltSpeed);

                // Acknowledge the command
                return new byte[] { 0x90, 0x50, 0xFF };
            }

            // Handle the Special Mode command: 81 01 7E 01 0A 00 03 FF
            if (MatchCommand(command, new byte[] { 0x81, 0x01, 0x7E, 0x01, 0x0A, 0x00, 0x03, 0xFF }))
            {
                isSpecialModeEnabled = true; // Enable Special Mode
                Console.WriteLine("Command: Enable Special Mode");
                return new byte[] { 0x90, 0x50, 0xFF }; // Acknowledge response
            }

            // Handle the custom command: 81 0A 04 47 07 00 01 02 0C FF
            if (MatchCommand(command.Take(4).ToArray(), new byte[] { 0x81, 0x0A, 0x04, 0x47 }))
            {
                // Parse the additional parameters
                byte param1 = command[4]; // 07
                byte param2 = command[5]; // 00
                byte param3 = command[6]; // 01
                byte param4 = command[7]; // 02
                byte param5 = command[8]; // 0C

                Console.WriteLine($"Command: Custom Inquiry or Action");
                Console.WriteLine($"Parameters: {param1:X2} {param2:X2} {param3:X2} {param4:X2} {param5:X2}");

                // Custom logic based on parameters (Example)
                if (param1 == 0x07 && param3 == 0x01)
                {
                    Console.WriteLine("Performing specific action based on parameters...");

                    // Simulated response (example values)
                    byte[] response = new byte[]
                    {
                        0x90, // Address set
                        0x50, // Command completion
                        0x00, 0x0A, 0x0B, 0x0C, // Example response data
                        0xFF  // End of message
                    };

                    Console.WriteLine($"Response: {BitConverter.ToString(response)}");
                    return response;
                }

                // Default response for unrecognized parameters
                return new byte[] { 0x90, 0x50, 0xFF };
            }

            Console.WriteLine("Unknown command received.");
            return null;
        }

        static byte[] ConvertPositionToViscaFormat(int position)
        {
            // Convert position to a 4-byte VISCA format
            int normalized = position; // No normalization needed for zoom
            return new byte[]
            {
                (byte)((normalized >> 12) & 0x0F), // Upper 4 bits of MSB
                (byte)((normalized >> 8) & 0x0F),  // Lower 4 bits of MSB
                (byte)((normalized >> 4) & 0x0F),  // Upper 4 bits of LSB
                (byte)(normalized & 0x0F)          // Lower 4 bits of LSB
            };
        }

        // Converts 4 VISCA bytes to an absolute position
        static int ConvertViscaBytesToPosition(byte b1, byte b2, byte b3, byte b4)
        {
            return ((b1 & 0x0F) << 12) | ((b2 & 0x0F) << 8) | ((b3 & 0x0F) << 4) | (b4 & 0x0F);
        }

        static int ConvertViscaBytesToPosition(byte highByte, byte lowByte)
        {
            // Convert two VISCA bytes into a position integer
            return ((highByte & 0x0F) << 12) | ((lowByte & 0x0F) << 8) | ((highByte & 0xF0) >> 4) | (lowByte & 0xF0) >> 4;
        }

        // Simulates moving to a position
        static void MoveToPosition(int panPosition, int tiltPosition, int panSpeed, int tiltSpeed)
        {
            // Update global positions (simulate camera movement)
            Console.WriteLine($"\nMoving to Pan: {panPosition}, Tilt: {tiltPosition} at speeds Pan: {panSpeed}, Tilt: {tiltSpeed}");
        }

        static bool MatchCommand(byte[] command, byte[] expected)
        {
            if (command.Length != expected.Length) return false;
            for (int i = 0; i < command.Length; i++)
            {
                if (command[i] != expected[i]) return false;
            }
            return true;
        }
    }
}
