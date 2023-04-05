using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms.DataVisualization.Charting;
using System.Drawing;

namespace WindowsFormsApp1
{

    public partial class Form1 : Form
    {
        System.Timers.Timer timer = new System.Timers.Timer();

        public UInt16 deltaT = 0;
        public double timeVal = 0;


        public Form1()
        {
            InitializeComponent();
            timer.AutoReset = true;
            timer.Elapsed += new ElapsedEventHandler(Reconnect);
            GC.Collect();
        }


        public void startBLEwatcher()
        {

            // Create Bluetooth Listener
            var watcher = new BluetoothLEAdvertisementWatcher();

            watcher.ScanningMode = BluetoothLEScanningMode.Active;

            // Only activate the watcher when we're recieving values >= -80
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = -100;

            // Stop watching if the value drops below -90 (user walked away)
            watcher.SignalStrengthFilter.OutOfRangeThresholdInDBm = -100;

            // Register callback for when we see an advertisements
            watcher.Received += OnAdvertisementReceived;

            // Wait 5 seconds to make sure the device is really out of range
            watcher.SignalStrengthFilter.OutOfRangeTimeout = TimeSpan.FromMilliseconds(5000);
            watcher.SignalStrengthFilter.SamplingInterval = TimeSpan.FromMilliseconds(500);

            // Starting watching for advertisements
            watcher.Start();
            SetText("Scanning...");
            this.Invoke((MethodInvoker)delegate { chart1.Series[0].Points.Clear(); });
        }

        public UInt16 ecFlex_idx;
        public UInt16 ecFlex_timer;
        public UInt16 ecFlex_temp;
        public UInt16 ecFlex_adc;
        public BluetoothLEDevice device = null;
        public string fileName = $"{Path.GetDirectoryName(Application.ExecutablePath)}\\Autosave.csv";
        public string newFileName = $"{Path.GetDirectoryName(Application.ExecutablePath)}\\Autosave.csv";
        public bool BLEdisconnectFlag = true;
        public Object[] sensorParam = new object[30]; // your initial array
        public bool senParamsFilled = false;

        //Added  By SS & Mezanur on Aug 2019
        public GattCharacteristic attribute_INT_Z_SHIFT;
        public GattCharacteristic attribute_OP_MODE_SHIFT;
        public GattCharacteristic attribute_TIA_GAIN_SHIFT;
        public GattCharacteristic attribute_SYS_SOFT_RESET;

        public static class Globals
        {
            public static bool DBLCLICK = false;
            public static string UUID_SERVICE = "00002d8d00001000800000805f9b34fb";
            public static string UUID_CHAR = "00002da700001000800000805f9b34fb";

        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            // Thread error workaround
            // Check if there are any manufacturer-specific sections.
            // If there is, print the raw data of the first manufacturer section (if there are multiple).
            string manufacturerDataString = "";
            var manufacturerSections = eventArgs.Advertisement.ManufacturerData;
            // eventArgs.Advertisement.ManufacturerData.Compan
            if (manufacturerSections.Count > 0)
            {
                var manufacturerData = manufacturerSections[0];
                var data = new byte[manufacturerData.Data.Length];
                using (var reader = DataReader.FromBuffer(manufacturerData.Data))
                {
                    reader.ReadBytes(data);
                }
                // Print the company ID + the raw data in hex format.
                manufacturerDataString = string.Format("0x{0}: {1}",
                    manufacturerData.CompanyId.ToString("X"),
                    BitConverter.ToString(data));
            }
            string str = string.Format("\n[{0}] [{1}]: Rssi={2}dBm, localName={3}, manufacturerData=[{4}]",
                eventArgs.Timestamp.ToString("hh\\:mm\\:ss\\.fff"),
                eventArgs.AdvertisementType.ToString(),
                eventArgs.RawSignalStrengthInDBm.ToString(),
                eventArgs.Advertisement.LocalName,
                manufacturerDataString);


            if (eventArgs.Advertisement.LocalName.Contains("ecFlex") || eventArgs.Advertisement.LocalName.Contains("SensorBLEPeripheral"))
            {
                listView1.Invoke((MethodInvoker)delegate ()
                {
                    //var selIndex = listView1.SelectedIndices[1];

                    // Update list with new items, refresh old
                    ListViewItem oldItem = listView1.FindItemWithText(Regex.Replace(eventArgs.BluetoothAddress.ToString("X"), "(.{2})(.{2})(.{2})(.{2})(.{2})(.{2})", "$1:$2:$3:$4:$5:$6"));
                    ListViewItem newItem = (new ListViewItem(new string[] { string.Format("{0}", eventArgs.Advertisement.LocalName), string.Format("{0}", eventArgs.AdvertisementType.ToString()), Regex.Replace(eventArgs.BluetoothAddress.ToString("X"), "(.{2})(.{2})(.{2})(.{2})(.{2})(.{2})", "$1:$2:$3:$4:$5:$6"), Convert.ToString(eventArgs.RawSignalStrengthInDBm), "Double click name to connect", Convert.ToString(eventArgs.BluetoothAddress) }));
                    if (oldItem == null)
                    {
                        listView1.Items.Add(newItem);
                    }
                    else
                    {
                        // Remove from list if power too weak / offline
                        if (eventArgs.RawSignalStrengthInDBm < -130)
                        {
                            listView1.Items[oldItem.Index].Remove();
                        }
                        else
                        {
                            listView1.Items[oldItem.Index].SubItems[0].Text = string.Format("{0}", eventArgs.Advertisement.LocalName);
                            listView1.Items[oldItem.Index].SubItems[3].Text = Convert.ToString(eventArgs.RawSignalStrengthInDBm);
                        }
                    }

                });
            }

        }


        private void button1_Click(object sender, EventArgs e)
        {
            Disconnect();
            startBLEwatcher();
        }

        delegate void SetTextCallback(string text);

        private void SetText(string text)
        {
            // InvokeRequired required compares the thread ID of the
            // calling thread to the thread ID of the creating thread.
            // If these threads are different, it returns true.
            if (this.textBox1.InvokeRequired)
            {
                SetTextCallback d = new SetTextCallback(SetText);
                this.Invoke(d, new object[] { text });
            }
            else
            {
                this.textBox1.Text = text;
            }
        }

        public async Task subscribeToNotify()
        {
            // Service
            Guid serviceGuid = Guid.Parse(Globals.UUID_SERVICE);
            GattDeviceServicesResult serviceResult = await device.GetGattServicesForUuidAsync(serviceGuid);

            device.ConnectionStatusChanged += OnConnectionChange;

            // Characteristic (Handle 17 - Sensor Command)
            Guid charGiud = Guid.Parse(Globals.UUID_CHAR);
            var characs = await serviceResult.Services.Single(s => s.Uuid == serviceGuid).GetCharacteristicsAsync();
            var charac = characs.Characteristics.Single(c => c.Uuid == charGiud);

            GattCharacteristicProperties properties = charac.CharacteristicProperties;


            //Write the CCCD in order for server to send notifications.               
            var notifyResult = await charac.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                      GattClientCharacteristicConfigurationDescriptorValue.Notify);

            charac.ValueChanged += Charac_ValueChangedAsync;

        }

        public async Task Connect(ulong addr)
        {
            SetText($"Connecting to {listView1.Items[listView1.FocusedItem.Index].Text}...");

            int retryCounter = 1;
            for (int i = 0; i < retryCounter; i++)
            {
                // Devices
                device = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);


                //device.ConnectionStatus

                if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                {
                    await Task.Delay(250);

                }
                else
                {
                    i = retryCounter;
                    SetText($"BLEWATCHER Found: {device.Name}");
                }


            }

            //// Service
            Guid serviceGuid = Guid.Parse(Globals.UUID_SERVICE);
            GattDeviceServicesResult serviceResult = await device.GetGattServicesForUuidAsync(serviceGuid);

            // Subscribe to connection change
            if (serviceResult.Status == GattCommunicationStatus.Success)
            {
                BLEdisconnectFlag = false;
                SetText($"Communicating: Success!");
            }
            device.ConnectionStatusChanged += OnConnectionChange;

            Guid charGiud = Guid.Parse(Globals.UUID_CHAR);
            var characs = await serviceResult.Services.Single(s => s.Uuid == serviceGuid).GetCharacteristicsAsync();

            int readCounter = 0;
            // Read all readable characteristics in this service
            foreach (var character in characs.Characteristics)
            {
                GattCharacteristicProperties properties = character.CharacteristicProperties;
                if ((properties.HasFlag(GattCharacteristicProperties.Read)) && (senParamsFilled == false)) // Chech if not already read
                {
                    var result = await character.ReadValueAsync();

                    CryptographicBuffer.CopyToByteArray(result.Value, out byte[] data);

                    switch (data.Length)
                    {
                        case 1:
                            sensorParam[readCounter] = data[0];
                            break;
                        case 2:
                            sensorParam[readCounter] = BitConverter.ToUInt16(data, 0);
                            break;
                        case 4:
                            sensorParam[readCounter] = BitConverter.ToInt32(data, 0);
                            break;
                        default:
                            sensorParam[readCounter] = Encoding.UTF8.GetString(data);
                            break;
                    }

                    //Added  By SS & Mezanur on Aug 2019


                    //*************************************************************************************************************************************************
                    //********************************************* Saving the Handle parameters ****************************************************************************
                    //**************************************************************************************************************************************************
                    if (character.AttributeHandle == 78)
                        attribute_OP_MODE_SHIFT = character;
                    if (character.AttributeHandle == 81)
                        attribute_TIA_GAIN_SHIFT = character;
                    if (character.AttributeHandle == 87)
                        attribute_INT_Z_SHIFT = character;
                    if (character.AttributeHandle == 108)
                        attribute_SYS_SOFT_RESET = character;


                    //Added  By SS & Mezanur on Aug 2019

                    //*************************************************************************************************************************************************
                    //********************************************* Value for op_mode in combobox2 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 19)
                    {

                        comboBox2.SelectedIndex = Array.IndexOf(new int[] { 0, 1, 2, 3, 6, 7 }, data[0]);

                    }

                    //*************************************************************************************************************************************************
                    //********************************************* Value for tia_gain in combobox3 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 20)
                    {
                        comboBox3.SelectedIndex = Array.IndexOf(new int[] { 0, 4, 8, 12, 16, 20, 24, 28 }, data[0]);
                    }

                    //*************************************************************************************************************************************************
                    //********************************************* Value for R_LOAD in combobox4 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 21)
                    {
                        comboBox4.SelectedIndex = data[0];
                    }

                    //*************************************************************************************************************************************************
                    //********************************************* Value for INT_Z in combobox1 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 22)
                    {
                        comboBox1.SelectedIndex = Array.IndexOf(new int[] { 0, 32, 64, 96 }, data[0]);
                    }

                    //*************************************************************************************************************************************************
                    //********************************************* Value for Bias Sign in combobox5 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 27)
                    {
                        comboBox5.SelectedIndex = Array.IndexOf(new int[] { 0, 16 }, data[0]);

                    }


                    //*************************************************************************************************************************************************
                    //********************************************* Value for Bias Voltage in combobox6 ****************************************************************************
                    //**************************************************************************************************************************************************

                    if (readCounter == 28)
                    {
                        comboBox6.SelectedIndex = data[0];
                    }

                    if (character.AttributeHandle == 33)
                    {
                        //if (!$"{sensorParam[readCounter]}".Contains("Chronoamperometry"))
                        if ($"{sensorParam[readCounter]}".Contains("Chronopotentiometry"))
                            groupBox1.Enabled = false;
                    }

                    SetText($"Read handle {character.AttributeHandle}: {sensorParam[readCounter]}");
                    if (readCounter > 28)
                    {
                        senParamsFilled = true;
                        this.Invoke((MethodInvoker)delegate { chart1.ChartAreas[0].Axes[1].IsLogarithmic = Convert.ToBoolean(sensorParam[18]); });
                        //this.Invoke((MethodInvoker)delegate { chart1.ChartAreas[0].AxisX.Minimum = calcSensorVal((UInt16)sensorParam[9]);
                        //                                      chart1.ChartAreas[0].AxisX.Maximum = calcSensorVal((UInt16)sensorParam[10]);
                        this.Invoke((MethodInvoker)delegate
                        {
                            if (checkBox1.Checked)
                            {
                                chart1.ChartAreas[0].Name = "Analyte concentration"; 
                                chart1.Titles.Add(textBox5.Text);
                                chart1.ChartAreas[0].AxisY.Title = textBox5.Text;
                            }
                            else
                            { 
                                chart1.ChartAreas[0].Name = Convert.ToString(sensorParam[4]);
                                chart1.Titles.Add(Convert.ToString(sensorParam[4]));
                                chart1.ChartAreas[0].AxisY.Title = Convert.ToString(sensorParam[7]);
                            }
                            
                            //chart1.ChartAreas[0].AxisX.Title = Convert.ToString(sensorParam[6]);
                            chart1.ChartAreas[0].AxisX.Title = "Time";
                            
                        });

                        File.WriteAllText(fileName, $"Start time: {DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")}{Environment.NewLine}");
                        File.WriteAllText(fileName, $"Counter,Relative time / s,Temperature / degC,{Convert.ToString(sensorParam[7])}{Environment.NewLine}");

                        //});


                    }

                    readCounter++;

                }

       
                // these are other sorting flags that can be used so sort characterisics.
                if (properties.HasFlag(GattCharacteristicProperties.Write))
                {
                    //SetText("This characteristic supports writing.");
                }
                if (properties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    // SetText("This characteristic supports subscribing to notifications.");
                    if (character.Uuid == charGiud) // sensorCommand
                    {
                        //Write the CCCD in order for server to send notifications.               
                        var notifyResult = await character.WriteClientCharacteristicConfigurationDescriptorAsync(
                                                                  GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        character.ValueChanged += Charac_ValueChangedAsync;

                    }
                }



            }
            if (Math.Abs((Convert.ToInt64(sensorParam[25]))) == 1e9)
            {
                listView2.Items[4].Text = "I/nA";
            }


            refreshGraphBounds();
            disableSensorButtons(false);
        }

        public async Task Disconnect()
        {
            BLEdisconnectFlag = true;
            //senParamsFilled = false;
            timer.Stop();
            // Service
            Guid serviceGuid = Guid.Parse(Globals.UUID_SERVICE);
            GattDeviceServicesResult serviceResult = await device.GetGattServicesForUuidAsync(serviceGuid);

            device.ConnectionStatusChanged -= OnConnectionChange;

            // Characteristic (Handle 17 - Sensor Command)
            Guid charGiud = Guid.Parse(Globals.UUID_CHAR);
            var characs = await serviceResult.Services.Single(s => s.Uuid == serviceGuid).GetCharacteristicsAsync();
            var charac = characs.Characteristics.Single(c => c.Uuid == charGiud);


            charac.ValueChanged -= Charac_ValueChangedAsync;
            device.Dispose();
            device = null;
            GC.Collect();
        }





        public struct Data
        {
            public short counter;
            public short time;
            public short temp;
            public short sens;

        }
        public void Charac_ValueChangedAsync(GattCharacteristic sender, GattValueChangedEventArgs args)
        {

            // Dont run if we do not wish to be connected anymore OR we are still waiting for all parameters
            if (BLEdisconnectFlag || !senParamsFilled) return;


            if (deltaT > 0)
            {
                timer.Interval = 3 * deltaT;
                timer.Start();
            }

            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] data);



            //Asuming Encoding is in ASCII, can be UTF8 or other!
            string dataFromNotify = Encoding.ASCII.GetString(data);
            byte[] bytes = Encoding.UTF8.GetBytes(dataFromNotify);
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            Data data1 = (Data)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Data));
            handle.Free();
            // SetText($"Handle 17: {data1.counter} - {(double)da ta1.time/1000} - {(double)data1.temp/10} - {data1.sens}");

            UInt16 old_ecFlex_timer = ecFlex_timer;
            UInt16 oldDeltaT = deltaT;
            deltaT = (UInt16)((UInt16)((data[3] << 8) + data[2]) - ecFlex_timer);
            if (deltaT == 0)
                deltaT = oldDeltaT;

            double oldTimeVal = (double)(deltaT * ecFlex_idx / 1000);
            ecFlex_idx = (UInt16)((data[1] << 8) + data[0]);        // Sample counter
            ecFlex_timer = (UInt16)((data[3] << 8) + data[2]);      // Timer in milliseconds
            ecFlex_temp = (UInt16)((data[5] << 8) + data[4]);       // Temperature * 10
            ecFlex_adc = (UInt16)((data[7] << 8) + data[6]);        // ADC value
            double sensorVal = calcSensorVal(ecFlex_adc);
            if (checkBox1.Checked)
                {
                sensorVal = Convert.ToDouble(textBox3.Text) * sensorVal + Convert.ToDouble(textBox4.Text);
                }




            if (oldTimeVal > 0) // No point of logging data if not ready
            {
                // Workaround for nonsensical time values
                if (((double)deltaT * (double)ecFlex_idx / 1000) > (3 * deltaT / 1000 + timeVal))
                {
                    timeVal = timeVal + (double)deltaT / 1000;
                }
                else
                {
                    timeVal = ((double)deltaT * (double)ecFlex_idx) / 1000;
                }
                SetText($"{ecFlex_idx} - {(deltaT * (float)ecFlex_idx / 1000)} - {(Single)ecFlex_temp / 10} - {ecFlex_adc}");

                this.Invoke((MethodInvoker)delegate { chart1.Series[0].Points.AddY(sensorVal); chart1.Series[0].Points.AddXY((Single)timeVal, sensorVal); });



                try
                {
                    Invoke((MethodInvoker)delegate { File.AppendAllText(fileName, $"{ecFlex_idx},{timeVal},{(Single)ecFlex_temp / 10},{sensorVal}{Environment.NewLine}"); });
                }
                catch (IOException er)
                {
                    //  if (er.Message.Contains("being used"))
                    { // Just append date if unable to access file.


                        string oldFileName = newFileName;
                        newFileName = ($"{fileName.Substring(0, fileName.Length - 4)}_{DateTime.Now.ToString("yyyyMMddHHmmss")}.csv").Replace("/", "_");
                        File.Copy(oldFileName, newFileName, true);

                        // Dont forget to log the new sample in the new file
                        this.Invoke((MethodInvoker)delegate { File.AppendAllText(newFileName, $"{ecFlex_idx},{timeVal},{(Single)ecFlex_temp / 10},{sensorVal}{Environment.NewLine}"); });

                    }
                }


            }



        }

        public void Reconnect(object sender, ElapsedEventArgs e)
        {
            timer.Stop();
            updateREFECN(); // Just try to write and see if we can wake up the connection again
            subscribeToNotify();
            SetText("Reconnecting...");

            // TODO Try write to device. This should re-establish connection if reachable
        }

        public void listView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (listView1.FocusedItem.Index >= 0)
            {//watcher
                Globals.DBLCLICK = true;
                ulong addr = Convert.ToUInt64(listView1.Items[listView1.FocusedItem.Index].SubItems[5].Text);

                // We do not need to set it up again if the user just clicks to reconnect
                if (senParamsFilled == false)
                {
                    this.Invoke((MethodInvoker)delegate { chart1.Series[0].Points.Clear(); });
                    this.Invoke((MethodInvoker)delegate
                    {

                        //chart1.Series[0].XValueType = System.Windows.Forms.DataVisualization.Charting.ChartValueType.DateTime;
                        //chart1.ChartAreas[0].AxisX.IntervalType = DateTimeIntervalType.Milliseconds;
                    });


                    Connect(addr);
                }
                else
                {

                    subscribeToNotify();
                }
                chart1.MouseWheel += chart1_MouseWheel;
                //textBox1.Text = "Connected!";
                //subscribeToNotify();
            }
        }


        public async void OnConnectionChange(BluetoothLEDevice bluetoothLEDevice, object args)
        {
            // Dont run if we do not wish to be connected anymore or we are still waiting for all parameters
            if (BLEdisconnectFlag || !senParamsFilled) return;

            SetText($"The device is now: {bluetoothLEDevice.ConnectionStatus}");
            subscribeToNotify();

        }


        private void button2_Click(object sender, EventArgs e)
        {
            Disconnect();
        }

        private double calcSensorVal(UInt32 ADC)
        {
            double D0 = Convert.ToDouble(sensorParam[0]); // ADC resolution * 100
            double N0 = Convert.ToDouble(sensorParam[1]); // ADC reference voltage in V * 100
            double X0 = Convert.ToDouble(sensorParam[2]); // Virtual ground level in V * 100
            double D1 = Convert.ToDouble(sensorParam[3]); // R_TIA * 100
            double N1 = Convert.ToDouble(sensorParam[25]); // Scale factor
            double D2 = Convert.ToDouble(sensorParam[26]); // Scale factor
            //  Int32 D2 = sensorParam[23]; // 
            D1 = D1; // Account for resistor tolerance
                     // X0 = 150;
            double Vout = (ADC / D0) * N0 - X0 / 100; // Volts


            double Iout = -100 * (Vout * N1 / D1);
            double val = Iout / D2;
            //double val = Vout * N1 / D1 / D2;
            this.Invoke((MethodInvoker)delegate
            {
                listView2.Items[0].SubItems[1].Text = deltaT.ToString("F1");
                listView2.Items[1].SubItems[1].Text = timeVal.ToString("F2");
                listView2.Items[2].SubItems[1].Text = (0.1 * ecFlex_temp).ToString("F1");
                listView2.Items[3].SubItems[1].Text = (1000 * Vout).ToString("F2"); // mV

                if (X0 != 0) // if there is no internal zero it is likely OCP measuremet
                {        
                    listView2.Items[4].SubItems[1].Text = Iout.ToString("F2"); // µA
                }
                if (Math.Abs(D2) > 1) // IF there is a calibration factor
                {
                    listView2.Items[5].SubItems[1].Text = val.ToString("F2"); // mM
                }
            });
            return val;
        }

        private void button3_Click(object sender, EventArgs e)
        {
            // Disconnect(); // Make sure we are not still logging data

            // Displays a SaveFileDialog so the user can save the file
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();
            saveFileDialog1.Filter = "Comma separated values|*.csv";
            saveFileDialog1.Title = "Save data";
            saveFileDialog1.ShowDialog();

            // If the file name is not an empty string open it for saving.  
            if (saveFileDialog1.FileName != "")
            {
                try
                {
                    File.Copy(fileName, saveFileDialog1.FileName, true);
                }
                catch (IOException er)
                {
                    if (er.Message.Contains("being used"))
                    {
                        SetText($"Error: Target file in use. File retreiveable at {fileName}");
                    }
                }
            }
        }

        Point? prevPosition = null;
        ToolTip tooltip = new ToolTip();



        private void Form1_Load(object sender, EventArgs e)
        {
            //chart1.ChartAreas["ChartArea1"].AxisX.Interval = 10.0;
            chart1.ChartAreas[0].AxisX.Minimum = 0;

            chart1.ChartAreas[0].AxisX.ScaleView.Zoomable = true;
            chart1.ChartAreas[0].AxisY.ScaleView.Zoomable = true;
            chart1.ChartAreas[0].CursorX.LineColor = Color.Black;
            chart1.ChartAreas[0].CursorX.LineWidth = 1;
            chart1.ChartAreas[0].CursorX.LineDashStyle = ChartDashStyle.Dot;
            chart1.ChartAreas[0].CursorX.Interval = 1;
            chart1.ChartAreas[0].CursorY.LineColor = Color.Black;
            chart1.ChartAreas[0].CursorY.LineWidth = 1;
            chart1.ChartAreas[0].CursorY.LineDashStyle = ChartDashStyle.Dot;
            chart1.ChartAreas[0].CursorY.Interval = 1;
            chart1.ChartAreas[0].AxisX.ScrollBar.Enabled = false;
            chart1.ChartAreas[0].AxisY.ScrollBar.Enabled = false;

            //chart1.ChartAreas[0].IsSameFontSizeForAllAxes = true;
            chart1.ChartAreas[0].AxisX.TitleFont = new Font("Arial", 10, FontStyle.Bold);
            chart1.ChartAreas[0].AxisY.TitleFont = new Font("Arial", 10, FontStyle.Bold);
            listView2.Items.Add($"\u2206Time/ms"); listView2.Items[0].SubItems.Add("");
            listView2.Items.Add("Time/s"); listView2.Items[1].SubItems.Add("");
            listView2.Items.Add("Temp/\u2103"); listView2.Items[2].SubItems.Add("");
            listView2.Items.Add("Vout/mV"); listView2.Items[3].SubItems.Add("");
            listView2.Items.Add("I/µA"); listView2.Items[4].SubItems.Add("");
            listView2.Items.Add("C/mM"); listView2.Items[5].SubItems.Add("");
            // listView2.Items.Add("Distance/m"); listView2.Items[3].SubItems.Add("");

        }

        private void chart1_MouseClick(object sender, MouseEventArgs e)
        {
            var pos = e.Location;
            if (prevPosition.HasValue && pos == prevPosition.Value)
                return;
            tooltip.RemoveAll();
            prevPosition = pos;
            var results = chart1.HitTest(pos.X, pos.Y, false,
                                            ChartElementType.DataPoint);
            foreach (var result in results)
            {
                if (result.ChartElementType == ChartElementType.DataPoint)
                {
                    var prop = result.Object as DataPoint;
                    if (prop != null)
                    {
                        var pointXPixel = result.ChartArea.AxisX.ValueToPixelPosition(prop.XValue);
                        var pointYPixel = result.ChartArea.AxisY.ValueToPixelPosition(prop.YValues[0]);

                        // check if the cursor is really close to the point (2 pixels around the point)
                        if (Math.Abs(pos.X - pointXPixel) < 2 &&
                            Math.Abs(pos.Y - pointYPixel) < 2)
                        {
                            tooltip.Show("X=" + prop.XValue.ToString("F2") + ", Y=" + prop.YValues[0].ToString("F2"), this.chart1,
                                            pos.X, pos.Y - 15);
                        }
                    }
                }
            }
        }

        private void dataGridView1_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void dataGridView1_CellContentClick_1(object sender, DataGridViewCellEventArgs e)
        {

        }

        private void chart1_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePoint = new Point(e.X, e.Y);

            chart1.ChartAreas[0].CursorX.SetCursorPixelPosition(mousePoint, true);
            chart1.ChartAreas[0].CursorY.SetCursorPixelPosition(mousePoint, true);

            // ...
        }

        private void chart1_MouseWheel(object sender, MouseEventArgs e)
        {
            var chart = (Chart)sender;
            var xAxis = chart.ChartAreas[0].AxisX;
            var yAxis = chart.ChartAreas[0].AxisY;

            try
            {
                if (e.Delta < 0) // Scrolled down.
                {
                    xAxis.ScaleView.ZoomReset();
                    yAxis.ScaleView.ZoomReset();
                }
                else if (e.Delta > 0) // Scrolled up.
                {
                    var xMin = xAxis.ScaleView.ViewMinimum;
                    var xMax = xAxis.ScaleView.ViewMaximum;
                    var yMin = yAxis.ScaleView.ViewMinimum;
                    var yMax = yAxis.ScaleView.ViewMaximum;

                    int zoomSens = 2;

                    var posXStart = xAxis.PixelPositionToValue(e.Location.X) - (xMax - xMin) / zoomSens;
                    var posXFinish = xAxis.PixelPositionToValue(e.Location.X) + (xMax - xMin) / zoomSens;
                    var posYStart = yAxis.PixelPositionToValue(e.Location.Y) - (yMax - yMin) / zoomSens;
                    var posYFinish = yAxis.PixelPositionToValue(e.Location.Y) + (yMax - yMin) / zoomSens;

                    xAxis.ScaleView.Zoom(posXStart, posXFinish);
                    yAxis.ScaleView.Zoom(posYStart, posYFinish);
                }
            }
            catch { }
        }




        public async void updateMODECN()
        {
            timer.Stop();

            //subscribeToNotify(false);
            int[] opModes = { 0, 1, 2, 3, 6, 7 };
            int opMode = opModes[comboBox2.SelectedIndex];

            int MODECN = opMode;

            var writer = new DataWriter();
            writer.WriteBytes(BitConverter.GetBytes(MODECN));
            attribute_OP_MODE_SHIFT.WriteValueAsync(writer.DetachBuffer());
            //subscribeToNotify(true);
            timer.Start();
        }

        public async void updateRTIACN()
        {
            timer.Stop();
            // subscribeToNotify(false);
            int rTIA = comboBox3.SelectedIndex << 2;
            int rLoad = comboBox4.SelectedIndex << 0;


            var REFECN = rTIA | rLoad;


            var writer = new DataWriter();
            writer.WriteBytes(BitConverter.GetBytes(REFECN));

            int[] RTIAoptions = { Convert.ToInt16(textBox2.Text) * 1000 * 100, 275000, 350000, 700000, 1400000, 3500000, 12000000, 35000000 };
            sensorParam[3] = RTIAoptions[comboBox3.SelectedIndex];


            attribute_TIA_GAIN_SHIFT.WriteValueAsync(writer.DetachBuffer());
            refreshGraphBounds();
            //subscribeToNotify(true);
            timer.Start();
        }

        public async void updateREFECN()
        {

            timer.Stop();
            // subscribeToNotify(false);
            int intZsel = comboBox1.SelectedIndex << 5;
            int biasSign = comboBox5.SelectedIndex << 4;
            int biasVoltage = comboBox6.SelectedIndex << 0;



            var REFECN = intZsel | biasSign | biasVoltage;


            var writer = new DataWriter();
            writer.WriteBytes(BitConverter.GetBytes(REFECN));

            int[] intZelOtions = { 60, 150, 201, 300 };
            sensorParam[2] = intZelOtions[comboBox1.SelectedIndex];


            attribute_INT_Z_SHIFT.WriteValueAsync(writer.DetachBuffer());


            refreshGraphBounds();
            //subscribeToNotify(true);
            timer.Start();

        }

        public void refreshGraphBounds()
        {
            double minY = calcSensorVal(1);
            double maxY = calcSensorVal(2048);
            if (chart1.ChartAreas[0].AxisY.Title.Contains("Potential"))
            {
                minY = minY * Convert.ToUInt32(sensorParam[3]) / 100;
                maxY = maxY * Convert.ToUInt32(sensorParam[3]) / 100;
            }

            double diff = (maxY - minY);
            chart1.ChartAreas[0].AxisY.Minimum = Math.Round(minY - 0.05 * diff, 1);
            chart1.ChartAreas[0].AxisY.Maximum = Math.Round(maxY + 0.05 * diff, 1);
        }

        private void button4_Click(object sender, EventArgs e)
        {
            updateREFECN();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            updateMODECN();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            updateRTIACN();
        }

        private void button7_Click(object sender, EventArgs e)
        {
            updateRTIACN();
        }

        private void button8_Click(object sender, EventArgs e)
        {
            updateREFECN();
        }

        private void button11_Click(object sender, EventArgs e)
        {
            updateREFECN();
        }

        private void button9_Click(object sender, EventArgs e)
        {
            byte[] SOFT_RESET = { 0x11 };
            var writer = new DataWriter();
            writer.WriteBytes(SOFT_RESET);
            timer.Stop();
            //subscribeToNotify(false);
            attribute_SYS_SOFT_RESET.WriteValueAsync(writer.DetachBuffer());

            restartApp();
        }

        public void disableSensorButtons(bool disableOrNot)
        {
            button9.Enabled = !disableOrNot;
            //groupBox1.Enabled = !disableOrNot;
            button2.Enabled = !disableOrNot;
        }


        public void restartApp()
        {
            Application.Restart();
            Environment.Exit(0);
        }

        public void comboBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox3.SelectedIndex == 0)
                textBox2.Enabled = true;
            else
                textBox2.Enabled = false;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {

        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBox1.Checked)
            {
                textBox5.Enabled = true;
                textBox3.Enabled = true;
                textBox4.Enabled = true;
            }
            else
            {
                textBox5.Enabled = false;
                textBox3.Enabled = false;
                textBox4.Enabled = false;
            }
        }
    }
}