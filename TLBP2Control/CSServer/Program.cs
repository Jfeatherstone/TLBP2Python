using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using Thorlabs.TLBP2.Interop;

namespace TLBP2PipeConnection
{
    class PipeServer
    {

        // This has to be the same in both python and c#, so should be constant
        private const string PIPE_NAME = "TLBP2PyConnection";
        private static TLBP2 bp2Device = null;

        static void Main(string[] args)
        {
            // Make sure the device is always stopped when this program ends
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(onProcessExit);

            // Check to see if we should suppress output or not
            // This is so that the program launched manually (for debugging)
            // will have useful info, but when launched from python it will be quiet
            bool suppressOutput = false;

            if (args.Length == 1)
            {
                if (args[0] == "--suppress-output")
                {
                    suppressOutput = true;
                }
            }

            try
            {
                if (!suppressOutput)
                    Console.WriteLine("Startup!");

                bp2Device = ConnectToFirstDevice();

                StringBuilder instrText = new StringBuilder(256);

                bp2Device.get_instrument_name(instrText);
                string devName = instrText.ToString();

                bp2Device.get_serial_number(instrText);
                string serialNum = instrText.ToString();

                if (!suppressOutput)
                {
                    Console.WriteLine("Connected to device: {0}!", devName);
                    Console.WriteLine("Serial Number: {0}", serialNum);

                    Console.Write("Ramping up drum speed...");
                }


                int status = RampUpDrumSpeed(bp2Device);

                // 4 means the drum speed is not stable
                ushort deviceStatus = 4;
                while (deviceStatus == 4 || deviceStatus == 5)
                {
                    bp2Device.get_device_status(out deviceStatus);
                    GetMeasurement(bp2Device);
                    Thread.Sleep(200);
                    if (!suppressOutput)
                        Console.WriteLine("Status {0}", deviceStatus);
                }
                // Doesn't matter too much, but the device status should switch to 3 after stabilization

                if (!suppressOutput)
                    Console.WriteLine("drum speed stablized!");

                // Now set up the server connection so we can communicate the measurements to python
                using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.InOut))
                {
                    if (!suppressOutput)
                        Console.Write("Attempting connection to pipe...");

                    pipeClient.Connect();

                    if (!suppressOutput)
                    {
                        Console.WriteLine("connection established!");
                        Console.WriteLine("Connected to pipe: {0}", PIPE_NAME);
                    }

                    using (StreamReader sr = new StreamReader(pipeClient))
                    {
                        StreamWriter sw = new StreamWriter(pipeClient);

                        string line;

                        while ((line = sr.ReadLine()) != null)
                        {
                            switch (line)
                            {
                                //////////////////////////
                                case "measure":
                                    if (!suppressOutput)
                                        Console.Write("Measuring...");
                                    string message = GetMeasurement(bp2Device);
                                    if (message.Length > 0)
                                    {
                                        //sw.WriteLine(message);
                                        sw.WriteLine(message);
                                        sw.Flush();
                                    }
                                    else
                                    {
                                        sw.WriteLine("Error measuring");
                                        sw.Flush();
                                    }
                                    if (!suppressOutput)
                                        Console.WriteLine("done!");
                                    break;

                                //////////////////////////
                                case "status":
                                    bp2Device.get_device_status(out deviceStatus);
                                    sw.WriteLine(deviceStatus);
                                    sw.Flush();
                                    if (!suppressOutput)
                                        Console.WriteLine("Status: {0}", deviceStatus);
                                    break;

                                //////////////////////////
                                case "stop":
                                    if (!suppressOutput)
                                        Console.Write("Stopping...");
                                    sw.WriteLine("Stopping");
                                    sw.Flush();
                                    bp2Device.Dispose();
                                    if (!suppressOutput)
                                        Console.WriteLine("done!");
                                    return;

                                //////////////////////////
                                default:
                                    if (!suppressOutput)
                                        Console.WriteLine("Unknown command: {0}", line);
                                    sw.WriteLine("Error");
                                    sw.Flush();
                                    break;
                            }
                        }

                        sw.Close();

                    }
                }

                bp2Device.Dispose();
                return;

            }
            catch (Exception ex)
            {
                // Ignore :)
                //Console.WriteLine(ex);
            }
            finally
            {
                bp2Device?.Dispose();
            }

        }

        /// <summary>
        /// search for connected devices and connect to the first one.
        /// Use only driver functions and structures.
        /// 
        /// Taken from the sample C# program provided by Thorlabs, probably found
        /// somewhere like:
        /// C:\Program Files (x86)\IVI Foundation\VISA\WinNT\TLBP2\Examples\MS VS 2012 CSharp Demo
        /// </summary>
        static private TLBP2 ConnectToFirstDevice()
        {
            TLBP2 bp2Device = null;
            int status;

            // intialize the driver class to call the pseudo static function "get_connected_devices"
            bp2Device = new TLBP2(new IntPtr());
            if (bp2Device != null)
            {
                uint deviceCount;
                status = bp2Device.get_connected_devices(null, out deviceCount);

                if (status == 0 && deviceCount > 0)
                {
                    bp2_device[] deviceList = new bp2_device[deviceCount];
                    status = bp2Device.get_connected_devices(deviceList, out deviceCount);

                    if (status == 0)
                    {
                        // connect to the first device
                        bp2Device = new TLBP2(deviceList[0].ResourceString, false, false);
                    }
                    else
                    {
                        bp2Device.Dispose();
                        bp2Device = null;
                    }
                }
                else
                {
                    bp2Device.Dispose();
                    bp2Device = null;
                }
            }

            return bp2Device;
        }

        static private int RampUpDrumSpeed(TLBP2 bp2Device)
        {
            // Ramp up the drum speed
            // I took this almost verbatim from the sample C# program Thorlabs provides, 
            // so I'm not exactly sure what it all does... :/

            // increase the drum speed
            ushort sampleCount;
            double sampleResolution;
            int status;

            status = bp2Device.set_drum_speed_ex(10.0, out sampleCount, out sampleResolution);

            // activate the position correction to have the same calculation results as the Thorlabs Beam Application
            status = bp2Device.set_position_correction(true);

            // activate the automatic gain calcuation
            status = bp2Device.set_auto_gain(true);

            // activate the drum speed correction
            status = bp2Device.set_speed_correction(true);

            // use the offset for 10Hz to be compatible with the release version 5.0
            status = bp2Device.set_reference_position(0, 4, 100.0);
            status = bp2Device.set_reference_position(1, 4, -100.0);
            status = bp2Device.set_reference_position(2, 4, 100.0);
            status = bp2Device.set_reference_position(3, 4, -100.0);

            return status;
        }

        /// <summary>
        /// poll for a new measurement and fill the structures with the calculation results.
        /// 
        /// Heavily modified, but originally taken from the sample C# program provided by Thorlabs,
        /// probably found somewhere like:
        /// C:\Program Files (x86)\IVI Foundation\VISA\WinNT\TLBP2\Examples\MS VS 2012 CSharp Demo
        /// 
        /// Status return codes:
        /// 0: Success, measurements provided in out params
        /// 1: Drum speed unstable, cannot perform measurements yet
        /// </summary>
        static private string GetMeasurement(TLBP2 bp2Device)
        {

            // get the drum speed
            double drumSpeed = 0;
            try
            {
                if (0 == bp2Device.get_drum_speed(out drumSpeed))
                {
                    //Console.WriteLine("Drum speed: {0}", drumSpeed.ToString("f2"));
                }
            }
            catch (System.Runtime.InteropServices.ExternalException ex)
            {
                // if the speed could not be measured -> this should cause no exception
                if (ex.ErrorCode != -1074001659)
                    throw ex;
            }

            // get the drum status
            ushort deviceStatus = 0;
            /*
            if (0 == bp2Device.get_device_status(out deviceStatus))
            {
                if ((deviceStatus & 4) == 4)
                {
                    // Drum is not stable, recurse
                    Thread.Sleep(50);
                    Console.WriteLine("Drum speed: {0}", drumSpeed.ToString("f2"));
                    WriteMeasurement(bp2Device, outFile);
                }
                else if ((deviceStatus & 2) == 2)
                {
                    // Drum speed is all good, can measure
                    // Nothing really to do though, just move on, but I'll leave this in to remind
                    // what the condition is for good speed.
                }

            }
            */

            // array of data structures for each slit.
            bp2_slit_data[] bp2SlitData = new bp2_slit_data[4];

            // array of calculation structures for each slit.
            bp2_calculations[] bp2Calculations = new bp2_calculations[4];

            // the gain and drum speed will be corrected during the measurement
            double power;
            float powerWindowSaturation;
            if (0 == bp2Device.get_slit_scan_data(bp2SlitData, bp2Calculations, out power, out powerWindowSaturation, null))
            {
                // X
                float peakPosX = bp2Calculations[0].PeakPosition;
                float centroidPosX = bp2Calculations[0].CentroidPos;
                float peakIntensityX = (bp2Calculations[0].PeakIntensity * 100.0f / ((float)0x7AFF - bp2SlitData[0].SlitDarkLevel));
                float widthX = bp2Calculations[0].BeamWidthClip;
                float gaussianFitCenterX = bp2Calculations[0].GaussianFitCentroid;
                float gaussianFitAmpX = bp2Calculations[0].GaussianFitAmplitude;
                float gaussianFitWidthX = bp2Calculations[0].GaussianFitDiameter;
                float gaussianFitDegreeX = bp2Calculations[0].GaussianFitPercentage;

                // Y
                float peakPosY = bp2Calculations[1].PeakPosition;
                float centroidPosY = bp2Calculations[1].CentroidPos;
                float peakIntensityY = (bp2Calculations[1].PeakIntensity * 100.0f / ((float)0x7AFF - bp2SlitData[1].SlitDarkLevel));
                float widthY = bp2Calculations[1].BeamWidthClip;
                float gaussianFitCenterY = bp2Calculations[1].GaussianFitCentroid;
                float gaussianFitAmpY = bp2Calculations[1].GaussianFitAmplitude;
                float gaussianFitWidthY = bp2Calculations[1].GaussianFitDiameter;
                float gaussianFitDegreeY = bp2Calculations[1].GaussianFitPercentage;


                List<string> lines = new List<string>();

                lines.Add("centroid=" + centroidPosX + "," + centroidPosY);
                lines.Add("peak=" + peakPosX + "," + peakPosY);
                lines.Add("peak_intensity=" + peakIntensityX + "," + peakIntensityY);
                lines.Add("drum_speed=" + drumSpeed);
                lines.Add("beam_width=" + widthX + "," + widthY);
                lines.Add("gaussian_fit_params_x=" + gaussianFitCenterX + "," + gaussianFitWidthX + "," + gaussianFitAmpX + "," + gaussianFitDegreeX);
                lines.Add("gaussian_fit_params_y=" + gaussianFitCenterY + "," + gaussianFitWidthY + "," + gaussianFitAmpY + "," + gaussianFitDegreeY);

                /*
                // This stuff ends up being too large to send easily over the pipe
                lines.Add("25um_slit_pos_x=" + string.Join(",", bp2SlitData[0].SlitSamplesPositions));
                lines.Add("25um_slit_int_x=" + string.Join(",", bp2SlitData[0].SlitSamplesIntensities));

                lines.Add("25um_slit_pos_y=" + string.Join(",", bp2SlitData[1].SlitSamplesPositions));
                lines.Add("25um_slit_int_y=" + string.Join(",", bp2SlitData[1].SlitSamplesIntensities));
                */
                /*
                lines.Add("25um_slit_data_x=");
                for (int i = 0; i < bp2SlitData[0].SlitSamplesPositions.Length; i++)
                {
                    lines.Add(bp2SlitData[0].SlitSamplesPositions[i] + "," + bp2SlitData[0].SlitSamplesIntensities[i]);
                }

                lines.Add("25um_slit_data_y=");
                for (int i = 0; i < bp2SlitData[1].SlitSamplesPositions.Length; i++)
                {
                    lines.Add(bp2SlitData[1].SlitSamplesPositions[i] + "," + bp2SlitData[1].SlitSamplesIntensities[i]);
                }
                */

                return string.Join("|", lines);
                //this.chart25um.Series[0].Points.DataBindXY(bp2SlitData[0].SlitSamplesPositions, bp2SlitData[0].SlitSamplesIntensities);
                //this.chart25um.Series[1].Points.DataBindXY(bp2SlitData[1].SlitSamplesPositions, bp2SlitData[1].SlitSamplesIntensities);
                //this.chart5um.Series[0].Points.DataBindXY(bp2SlitData[2].SlitSamplesPositions, bp2SlitData[2].SlitSamplesIntensities);
                //this.chart5um.Series[1].Points.DataBindXY(bp2SlitData[3].SlitSamplesPositions, bp2SlitData[3].SlitSamplesIntensities);
            }

            return "";
        }

        static void onProcessExit(object sender, EventArgs e)
        {
            bp2Device?.Dispose();
        }
    }

}
