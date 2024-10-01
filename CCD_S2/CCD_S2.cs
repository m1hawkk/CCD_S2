using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Oracle.ManagedDataAccess.Client;

namespace CCD_S2
{
    public partial class CCD_S2 : Form
    {

        //private const string ConnectionString = @"Data Source=(DESCRIPTION =(ADDRESS = (PROTOCOL = TCP)(HOST = 10.41.200.220)(PORT = 1521))(CONNECT_DATA = (SERVER = DEDICATED)(SERVICE_NAME = vnsfcs)));User Id=VN;Password=VN#sfcs;";
        private static string ConnectionString = @"Data Source=(DESCRIPTION =(ADDRESS = (PROTOCOL = TCP)(HOST = 10.41.200.222)(PORT = 1521))(CONNECT_DATA = (SERVER = DEDICATED)(SERVICE_NAME = sfcsdbt)));User Id=VN;Password=VN#sfcs;";
        private static string connESH = @"Data Source=(DESCRIPTION =(ADDRESS = (PROTOCOL = TCP)(HOST = 10.5.1.53)(PORT = 1621))(CONNECT_DATA = (SERVER = DEDICATED)(SERVICE_NAME = erpdw)));User Id=GPPROD;Password=SWEETCAT;";
        private static string customerPN = "";
        private static string gemtekPN = "";
        private static string reelID = "";
        private static string qty = "";
        private static string datecode = "";
        private static string lotcode = "";
        private static string desc = "";
        private static string MSL = "";
        string responsedata = "";
        string reportdata = "";
        private static string PcName = System.Net.Dns.GetHostName();
        private static string UserName = System.Net.Dns.GetHostName();
        private static int port = 4400;
        private static string typeReceipt = "";
        private static string receiptno = "";
        public CCD_S2()
        {
            InitializeComponent();
            //ngăn trạng thái focus trên textbox này (hoặc chặn sự kiện Enter) -> để textbox chỉ đọc
            txt_ccd.Enter += (s, e) => { this.ActiveControl = null; };
            PreventMultipleInstances();
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
        }
        private void PreventMultipleInstances()
        {
            string processName = Process.GetCurrentProcess().ProcessName;
            Process[] existingProcesses = Process.GetProcessesByName(processName);
            if (existingProcesses.Length > 1)
            {
                MessageBox.Show("Program is open", "Message");
                Environment.Exit(2);
            }
        }
        string Receive(Socket socket, int timeout)
        {
            string result = "";
            //dùng để kiểm soát hành vi của socket khi cố gắng nhận dữ liệu -> quyết định khoảng thời gian tối đa mà method sẽ đợi để nhận dữ liệu trước khi kết thúc
            socket.ReceiveTimeout = timeout;
            byte[] buffer = new byte[1024];
            int length = 0;
            try
            {
                if ((length = socket.Receive(buffer)) > 0)
                {
                    result = Encoding.UTF8.GetString(buffer, 0, length);   //, 0, buffer.Length
                }
            }
            catch (Exception ex)
            {

            }
            return result;
        }

        //Lấy mã PNcủa công ty (Gemtek_PN) tương ứng với mã PN của khách hàng (Customer_PN)
        private string GetGemtekPN(string customerPN)
        {
            using (OracleConnection conn = new OracleConnection(ConnectionString))
            {
                conn.Open();
                string query = "SELECT GEMTEK_PN FROM GM1_CUSTOMER_COMPONETPARTS WHERE CUSTOMER_PN = :customerPN AND ENABLED = 'Y' and type = :typeReceipt";
                using (OracleCommand cmd = new OracleCommand(query, conn))
                {
                    cmd.Parameters.Add(new OracleParameter("customerPN", customerPN));
                    cmd.Parameters.Add(new OracleParameter("typeReceipt", typeReceipt));
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        return reader.Read() ? reader["GEMTEK_PN"].ToString() : null;
                    }
                }
            }
        }

        //Tạo ReelID mới cho PN
        private string GetReelID(string vendorCode, string pn)
        {
            using (OracleConnection conn = new OracleConnection(ConnectionString))
            {
                conn.Open();
                string sql = "SELECT pvs.get_ctn_id('" + vendorCode.Trim() + "','" + pn + "','1010') reelid from dual";
                using (OracleCommand cmd = new OracleCommand(sql, conn))
                {
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        return reader.Read() ? reader["reelid"].ToString() : null;
                    }
                }
            }
        }

        //nối thành 1 chuỗi để ghi vào file txt
        public string ConcatReelPrintData(string reel, string pn, string qty, string dateCode, string lotCode,
                            string desc, string msl, string extendTimes, string mrb, string newPn, string oriQty)
        {
            List<string> data = new List<string>();
            data.Add(reel); //1
            data.Add(pn); //2
            data.Add(qty); //3
            data.Add(dateCode); //4
            data.Add(lotCode); //5

            data.Add("\"" + desc + "\""); //6
            data.Add("CCD"); //7
            data.Add(msl); //8
            data.Add(extendTimes); //9
            data.Add(mrb); //10

            data.Add(newPn); //11
            data.Add(pn); //12
            data.Add(oriQty); //13

            return String.Join(",", data.ToArray());
        }

        //Ghi dữ liệu vào file txt
        public void WriteLabelFile(string str)
        {
            string LabelDataPath = @"D:\Label\Temp.txt";
            Thread.Sleep(500);
            string[] arrTemp = LabelDataPath.Split('\\');
            string dir = "";
            for (int i = 0; i < arrTemp.Length - 1; i++)
            {
                dir += arrTemp[i] + "\\";
            }
            Directory.CreateDirectory(dir);

            using (FileStream fs = File.Create(LabelDataPath))
            {
                byte[] info = new UTF8Encoding(true).GetBytes(str);
                fs.Write(info, 0, info.Length);
            }
        }


        //lấy dữ liệu từ file txt, ghi vào file tem và in ra
        public void printLabel()
        {
            string LabelDataPath = @"D:\Label\Temp.txt";
            string LabelFilePath = @"D:\Label\ReelID_Asus.btw"; //file label
            string LabelProgramPath = @"C:\Program Files (x86)\Seagull\BarTender\7.75\bartend.exe"; //bartender programs
            if (!File.Exists(LabelFilePath))//check label path has exits
            {
                MessageBox.Show("Not found Label file");
                return;
            }
            if (!File.Exists(LabelProgramPath)) //check bartender programs
            {
                MessageBox.Show("Not found bartender program");
                return;
            }

            Process test = Process.Start(LabelProgramPath, @"/F=" + LabelFilePath + @" /D=" + LabelDataPath + " /P/X");

            while (!test.HasExited)
            {
                test.Refresh();
                Thread.Sleep(1000);
            }
            test.Close();
        }
        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                var listenersocket = e.Argument as Socket;  //lấy biến được truyền từ sự kiện LoadForm
                Socket acceptSocket = listenersocket.Accept();//接收第一条信息 Tạo Socket để nhận dữ liệu (Tạo 1 cái tai nghe để lắng nghe dữ liệu)
                string data = Receive(acceptSocket, 500);//超时时间500ms    Chuyển đổi dữ liệu nghe được thành chuỗi string      P07G001017120||Q1000||D2433||L20240922
                string requestTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");

                string[] parts = data.Split(new[] { "||" }, StringSplitOptions.None);
                if (parts.Length != 4)
                {
                    reportdata = data;
                    responsedata = "Invalid CCD Format";
                    responsedata += "\n-----------------------------------------------\n";
                    backgroundWorker1.ReportProgress(0);
                    backgroundWorker1.CancelAsync();
                    acceptSocket.Send(Encoding.Default.GetBytes(responsedata)); //发送给客户端是否可放行
                }
                else
                {
                    customerPN = parts[0].Substring(1); // Bỏ ký tự đầu Substring
                    qty = parts[1].Substring(1);
                    datecode = parts[2].Substring(1);
                    lotcode = parts[3].Substring(1);
                    gemtekPN = GetGemtekPN(customerPN);
                    if (gemtekPN == null)
                    {
                        responsedata = "Not found Gemtek_PN from table GM1_CUSTOMER_COMPONETPARTS where Customer_PN = " + customerPN;
                        responsedata += "\n-----------------------------------------------\n";
                    }
                    else
                    {
                        reelID = GetReelID("00000", gemtekPN);
                        //get description của PN này
                        string sqlDesc = @"Select Description From XXSFCS_MTL_System_Items@db2erp Where Organization_ID='1010' And Item='" + gemtekPN + "'";
                        desc = "";
                        using (OracleConnection conn = new OracleConnection(ConnectionString))
                        {
                            conn.Open();
                            using (OracleCommand cmd = new OracleCommand(sqlDesc, conn))
                            {
                                using (OracleDataReader reader = cmd.ExecuteReader())
                                {
                                    desc = reader.Read() ? reader["Description"].ToString() : null;
                                }
                            }
                        }

                        //get MSL
                        string sqlMSL = "Select MSL From V_MATE_MSL_SFCS Where Gem_Part_No IN ('" + gemtekPN + "')"; // querry bên database EHS để lấy MSL của PN này
                        MSL = "";
                        using (OracleConnection conn = new OracleConnection(connESH))
                        {
                            conn.Open();
                            using (OracleCommand cmd = new OracleCommand(sqlMSL, conn))
                            {
                                using (OracleDataReader reader = cmd.ExecuteReader())
                                {
                                    MSL = reader.Read() ? reader["MSL"].ToString() : null;
                                }
                            }
                        }

                        #region print Label
                        //nối thành 1 chuỗi để ghi vào file txt
                        string printdata = ConcatReelPrintData(reelID, gemtekPN, qty, datecode, lotcode, desc, "", MSL, "", "", "") + "\r\n";
                        //Ghi dữ liệu vào file txt
                        WriteLabelFile(printdata);
                        //lấy dữ liệu từ file txt ghi vào file tem và in ra
                        printLabel();

                        //Insert lịch sử in vào PVS.PVS_REEL_PRINT_LOG
                        string sqlInsertPrintLog = "INSERT INTO PVS.PVS_REEL_PRINT_LOG(REEL_ID,ACTION,PRINT_DATE,PRINT_USER,COMPUTER_NAME,PROGRAM_NAME) " +
                                                 "values('" + reelID + "','CCD',sysdate,'" + UserName + "','" + PcName + "','CCD')";
                        try
                        {
                            using (OracleConnection connection = new OracleConnection(ConnectionString))
                            {
                                connection.Open();
                                OracleTransaction transaction = connection.BeginTransaction();
                                try
                                {
                                    using (OracleCommand cmdOracle = new OracleCommand(sqlInsertPrintLog, connection))
                                    {
                                        int t = cmdOracle.ExecuteNonQuery();
                                    }
                                    transaction.Commit();
                                }
                                catch
                                {
                                    transaction.Rollback();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new Exception("Can't update Table" + ex.Message + "\n" + sqlInsertPrintLog);     //Có thể không cần return readerOracle
                        }
                        #endregion

                        responsedata = "OK";
                        //responsedata = "Get new ReelID: " + reelID;
                        responsedata += "\n-----------------------------------------------\n";
                    }
                    //reportdata = data;
                    reportdata = gemtekPN + " " + customerPN + " " + qty + " " + datecode + " " + lotcode;
                    backgroundWorker1.ReportProgress(0);
                    backgroundWorker1.CancelAsync();
                    acceptSocket.Send(Encoding.Default.GetBytes(responsedata)); //发送给客户端是否可放行
                }
                if (acceptSocket.Connected)
                {
                    acceptSocket.Shutdown(SocketShutdown.Both);
                    acceptSocket.Close();
                }
                //writer.Flush();
                acceptSocket.Close();
            }
        }

        private void backgroundWorker1_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            txt_ccd.Text = reportdata;
            rtbStatus.Text += "Response From Server: " + responsedata;
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
                txt_ccd.Text = "";
        }

        private void btn_ok_Click(object sender, EventArgs e)
        {

            if (receipttb.Text == "")
            {
                MessageBox.Show("Please enter Receipt No.");
                return;
            }
            receiptno = receipttb.Text;
            string typereceiptsql = @"Select Distinct Product_Line From Apps.Gm_Mtl_Customer_Item_V@Erpxxsfcs Mci Where Mci.Customer_Number = 33193 
                       And Mci.Item In (Select Item From Xxsfcs_Wms_Receipts_V@Erpxxsfcs Where Organization_Id = '1010' And Receipt_Num = '" + receiptno + "' And Quantity_Received <> 0)";
            using (OracleConnection conn = new OracleConnection(ConnectionString))
            {
                conn.Open();
                using (OracleCommand cmd = new OracleCommand(typereceiptsql, conn))
                {
                    using (OracleDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            typeReceipt = reader["PRODUCT_LINE"].ToString();
                            if (reader.Read())
                            {
                                MessageBox.Show("There are more than one type of this receipt. Please check again.");
                                return;
                            }
                            //MessageBox.Show(typeReceipt);
                        }
                        else
                        {
                            MessageBox.Show("Can't find type of this receipt. Please enter new Receipt No.");
                            return;
                        }
                    }
                }
            }
            //MessageBox.Show(typeReceipt);
            //return;
            Socket listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //设定IP地址和端口号
            listenSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            listenSocket.Listen(5);//只接受5条信息数据排队
            backgroundWorker1.RunWorkerAsync(listenSocket);
        }
    }
}
