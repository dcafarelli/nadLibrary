using System;
using System.Text;
using System.Collections.Generic;

using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Linq;

namespace NadLibrary
{
    
    public class NadSystem
    {
        Socket tcpServer;
        //Dictionary<string, Socket> dic = new Dictionary<string, Socket>();
        Dictionary<string, Socket> dic;
        private static List<string> _remoteIp = new List<string>();
        private List<string> _coord = new List<string>();
        string receivedMsg;                         

        //TODO: trovare modo per visualizare dettagli e messaggi connessione (per ora Console.WrileLine)
        //TODO: cambiare modo di recupeare ip del client (eliminare uso lista)

        //------------------- SYSTEM CONFIGURATION -------------------
        public void NADOpenConnection(string ip, string port_s)
        {
        
            /* Apre una connessione TCP e RTMP con il radiocomando/Jetson (client) 
             * per la comunicazione dei vari eventi e la ricezione del video
             */

            IPEndPoint iPEnd = new IPEndPoint(IPAddress.Parse(ip), Int32.Parse(port_s));

            try{
                tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                tcpServer.Bind(iPEnd);
                tcpServer.Listen(10);

                //var tcpThread = new Thread(new ParameterizedThreadStart(TCPServerConnect));
                Thread tcpThread = new(TCPServerConnect)
                {
                    IsBackground = true,
                    Name = "TCP Server Thread"
                };
                tcpThread.Start();

            }
            catch (SocketException e)
            {
                System.Diagnostics.Trace.WriteLine("Connection exception: {0}", e.ToString());
            }

        }

        private void TCPServerConnect(object obj)
        {
            System.Diagnostics.Trace.WriteLine("TCP server thread started");
            Console.WriteLine("TCP server thread started");
            try{
            
                while (true)
                {
                    Socket tcpClient = tcpServer.Accept();
                    string RemoteIP = tcpClient.RemoteEndPoint.ToString();
                    Console.WriteLine(RemoteIP + " Connected");
                    System.Diagnostics.Trace.WriteLine(RemoteIP + " Connected");
                    dic = new Dictionary<string, Socket>();
                    dic.Add(RemoteIP, tcpClient);
                    _remoteIp.Add(RemoteIP);

                    Thread receiveThread = new(Receive_tcp_msg)
                    {
                        IsBackground = true,
                        Name = "TCP Receive Thread"
                    };
                    receiveThread.Start(tcpClient);
                }
            }catch(SocketException e)
            {
                Console.WriteLine("SocketException: {0}", e);
            }            
        }

        public List<string> RemoteIP()
        {
            return _remoteIp;
        }


        private void Receive_tcp_msg(object soc)
        {
            try
            {
                Socket client = (Socket)soc;
                while (true)
                {
                    byte[] buffer = new byte[1024];
                    int n = client.Receive(buffer);

                    receivedMsg = Encoding.UTF8.GetString(buffer, 0, n);
                    //string msg = Encoding.UTF8.GetString(buffer, 0, n);
                    
                    Console.WriteLine(client.RemoteEndPoint.ToString() + ":" + receivedMsg);
                    System.Diagnostics.Trace.WriteLine(client.RemoteEndPoint.ToString() + ":" + receivedMsg);

                    if (receivedMsg.Equals("") || (receivedMsg.Equals("closing drone connection...")) || n == 0)
                    {
                        break;
                    }
                    if (receivedMsg.Equals("mob_mission"))
                    {
                        NADSendListCoord();
                    }
                }
                client.Close();
            }
            catch (SocketException se)
            {
                System.Diagnostics.Trace.WriteLine("SocketException : {0}", se.ToString());
                if (se.ErrorCode == 10053)
                {
                    string msg = "Drone disconnected";
                    System.Diagnostics.Trace.WriteLine(msg);
                    var first = dic.First();
                    string ip = first.Key;
                    //dic[ip].Shutdown(SocketShutdown.Both); //Cannot access a disposed object
                    dic[ip].Close();
                }      
            }                
        }

        public void Send_tcp_msg(string msg)
        {
            //string ip = lstboxIP.SelectedValue.ToString(); modificare e farlo inserire poi l'ip
            byte[] rsp = Encoding.Default.GetBytes(msg);

            var first = dic.First();
            string ip = first.Key;
            //string ip = _remoteIp[0];
            try
            {
                dic[ip].Send(rsp, 0);
            } catch (SocketException se)
            {
                System.Diagnostics.Trace.WriteLine("SocketException : {0}", se.ToString());
                if (se.ErrorCode == 10053)
                {
                    string err = "Drone disconnected";
                    System.Diagnostics.Trace.WriteLine(err);
                    dic[ip].Shutdown(SocketShutdown.Both);
                    dic[ip].Close();
                    dic.Remove(ip);
                }
            }

        }


        public void NADCloseConnection()
        {
            var first = dic.First();
            string ip = first.Key;
            //string ip = _remoteIp[0];
            System.Diagnostics.Trace.WriteLine("Dic[IP]" + ip);
            if (dic[ip] != null)
            {
                try
                {
                    //Send_tcp_msg("closing connection");
                    //dic[ip].Shutdown(SocketShutdown.Both);
                    dic[ip].Shutdown(SocketShutdown.Send);
                }
                catch (SocketException se)
                {
                    System.Diagnostics.Trace.WriteLine("SocketException : {0}", se.ToString());
                }
                finally
                {
                    dic[ip].Close();
                    if (tcpServer!= null)
                    {
                        tcpServer.Close();
                    }
                    
                    System.Diagnostics.Trace.WriteLine("TCP Server closed");
                }
            }
            else if (tcpServer!=null)
            {
                tcpServer.Close();
            }
        }

        public DateTime NADGetNetworkTime()
        {
            /* Restituisce data interna del server
             */

            //default Windows time server
            const string ntpServer = "ntp1.inrim.it";

            var ntpData = new byte[48];

            //Setting the Leap Indicator, Version Number and Mode values
            ntpData[0] = 0x1B; //LI = 0 (no warning), VN = 3 (IPv4 only), Mode = 3 (Client Mode)

            var addresses = Dns.GetHostEntry(ntpServer).AddressList;
            var ipEndPoint = new IPEndPoint(addresses[0], 123);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            socket.Connect(ipEndPoint);
            socket.Send(ntpData);
            socket.Receive(ntpData);
            socket.Close();

            ulong intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | (ulong)ntpData[43];
            ulong fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | (ulong)ntpData[47];

            var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
            var networkDateTime = (new DateTime(1900, 1, 1)).AddMilliseconds((long)milliseconds);

            return networkDateTime;
        }
        /* 
        public DateTime NADGetTime()
        {

            restituisce data interna del drone
             

            //event_msg='get_time'

            return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        */

        public void NADSetSpeed(string speed) //OK
        {
            /* Setta il valore di speed del drone
             * durante la ricerca (waypoint mission)
             * event_msg='waypoint_speed
             */
            String msg = "waypoint_speed" + "-" + speed +"\n\r";
            Send_tcp_msg(msg);
            /*
        
            if (!String.IsNullOrEmpty(speed) & IsNumeric(speed))
            {
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append("waypoint_speed").Append(",").Append(speed).Append("\r\n");
                Send_tcp_msg(stringBuilder.ToString());

            } else
            {
                Send_tcp_msg("null_speed");

            }*/
        }

        public void NADSetInterdictionArea(string area)
        {
            /* Setta il raggio dell'area di interdizione 
             * dalla ricerca di persone da parte del drone
             */
            String msg = "interdiction_area" + "-" + area + "\n\r";
            Send_tcp_msg(msg);
        }

        
        public void NADSendWarning(string warning)
        {
            /* Setta il raggio dell'area di interdizione 
             * dalla ricerca di persone da parte del drone
             */
            String msg = "warning" + "-" + warning + "\n\r";
            Send_tcp_msg(msg);
        }

        public void NADSearchNextTarget()
        {
            /* Setta il raggio dell'area di interdizione 
             * dalla ricerca di persone da parte del drone
             */
            String msg = "next_target\n\r";
            Send_tcp_msg(msg);
        }

        //------------------- MISSION REGION -------------------

        public List<string> getListCoordinate()
        {
            return _coord;
        }

        public void NADPopulateListCoordinate(string latitude, string longitude, string altitude)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("waypoint_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("-")
                .Append(altitude).Append("\n\r");

            _coord.Add(stringBuilder.ToString());
        }


        public void NADSendListCoord()
        {
            if (!getListCoordinate().Any())
            {
                Send_tcp_msg("Empty list");
            } else
            {
                foreach (var c in getListCoordinate())
                {
                    Send_tcp_msg(c);

                }
                string msg = "start_waypoint_list\n\r";
                Send_tcp_msg(msg);
            }

        }

        
        private void NADSendCoordinate(string latitude, string longitude, string altitude) //OK
        {
            /* Invia coordinate gps al drone per la ricerca 
             * invocare più volte se si vuole creare una lista di coordinate 
             */
             
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("waypoint_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("-")
                .Append(altitude).Append("\n\r");

            Send_tcp_msg(stringBuilder.ToString());

            //TODO: inserire controllo se latitude empty or ecc
        }
        

        
        private void NADUploadMobMission() //OK
        {
            /*carica e prepare il drone per il volo
             * verso le diverse coordinate
             */
             
            string msg = "upload_waypoint"+"\n\r";
            Send_tcp_msg(msg);
        }
        

        public void NADStartMobMission() //OK
        {
            /* Iinizia il volo
            verso le coordinate precedentemente caricata
             */
            NADSendListCoord();
            string msg = "start_waypoint"+"\n\r";
            Send_tcp_msg(msg);
         
            //event_msg='start_waypoint'

        }

        public void NADPauseMobMission() //OK
        {
            /* Mette in pausa il volo
            verso le coordinate precedentemente caricata
             */
            string msg = "pause_waypoint" + "\n\r";
            Send_tcp_msg(msg);

            //event_msg='pause_waypoint'
        }

        public void NADResumeMobMission() //OK
        {
            /* Mette in pausa il volo
            verso le coordinate precedentemente caricata
             */
            string msg = "resume_waypoint" + "\n\r";
            Send_tcp_msg(msg);

            //event_msg='resume_waypoint'
        }

        public void NADStopMobMission() //OK
        {
            /* Stoppa il volo
            verso le coordinate precedentemente caricata
             */
            string msg = "stop_waypoint" + "\n\r";
            Send_tcp_msg(msg);

            //event_msg='stop_waypoint'

        }

       
         public void NADDelPos()
         {
            string msg = "del_pos" + "\n\r";
            Send_tcp_msg(msg);
            //event_msg='del_pos'
         }


        public void NADSearchAtPos(string latitude, string longitude, string altitude, string radius)
        {
            /*
            Cerca un uomo in mare intorno a quella posizione
            * (Hot Point Mission)
            */
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("hotpoint_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("-")
                .Append(altitude).Append("-")
                .Append(radius).Append("\n\r");

            Send_tcp_msg(stringBuilder.ToString());
        }


/*        public void NADPauseSearchAt() //OK
        {
            *//* Mette in pausa il volo
            verso l'hotpoint
             *//*
            string msg = "pause_hotpoint" + "\n\r";
            Send_tcp_msg(msg);
        }*/

/*        public void NADResumeSearchAt() //OK
        {
            *//* Riprende il volo
            verso l'hotpoint
             *//*
            string msg = "resume_hotpoint" + "\n\r";
            Send_tcp_msg(msg);
        }*/

/*        public void NADStopSearchAt() //OK
        {
            *//* Ferma il volo
            verso l'hotpoint
             *//*
            string msg = "stop_hotpoint" + "\n\r";
            Send_tcp_msg(msg);
        }*/

        /*
        public void NADSearchNext()
        {
            /*Abbandona ricerca uomo in mare 
             * e continua con l'esecuzione (es. continua waypoint mission)
             

            //event_msg='search_next'
        }
        */

        /* 
        public void NADSearchBack(List<double> waypointsList)
        {
            Cerca a ritroso partendo dalla posizione più recente
             * fino ad arrivare a quella della barca
             * (WayPoint Mission)
             

            //event_msg='search_back'

        }
        */


        public void NADFollowShip(string latitude, string longitude, string altitude)
        {
            /*
             *Segue lo yatch
             * (Follow Me Mission)
             */

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("follow_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("-")
                .Append(altitude).Append("\n\r");

            Send_tcp_msg(stringBuilder.ToString());
        }

        public void NADUpdateShipCoord(string latitude, string longitude)
        {
            /*
             *Aggiorna la posizione della barca
             *
             * (Follow Me Mission)
             */

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("update_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("\n\r");

            Send_tcp_msg(stringBuilder.ToString());
        }

        public void NADStopFollowShip() //OK
        {
            /* Ferma il following della nave
             */
            string msg = "stop_follow" + "\n\r";
            Send_tcp_msg(msg);
        }

/*        public void NADGoToShip(string latitude, string longitude, string altitude)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("boat_coordinates").Append("-")
                .Append(latitude).Append("-")
                .Append(longitude).Append("-")
                .Append(altitude).Append("\n\r");

            Send_tcp_msg(stringBuilder.ToString());
        }*/


        //------------------- CALLS FROM SUPERVISOR SYSTEM -------------------

        public string NADGetDroneStatus()
        {

            /* Invia una lista contentente
             * gli stati del sistema del drone
             */

            string msg = "status"+"\n\r";
            Send_tcp_msg(msg);

            return receivedMsg;
        }

        /*
        public void NADGetVideoStrem()
        {
            /* Invia video streaming alla plancia
             * 
             * (definire formato video, come inviarlo, ecc..)
             
        }
        */


        //------------------ UTILITY FUNCTIONS -----------------------
        private bool IsNumeric(string text)
        {
            double _out;
            return double.TryParse(text, out _out);
        }

        public string GetMSG()
        {
            return receivedMsg;
        }

    }
}