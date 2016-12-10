/*
 * Copyright 2016 Gordon D. Thompson
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Data.SQLite;
using Microsoft.VisualBasic.FileIO;

namespace ov2optimizer
{
    public partial class Form1 : Form
    {
        static string hkcuKeyName = @"HKEY_CURRENT_USER\Software\gordthompson.com\ov2optimizer";
        SQLiteConnection dbConn;
        int limitE; int limitW; int limitN; int limitS;  // define boundaries of overall rectangle for each POI file
        int processedCount = 0;

        public Form1()
        {
            InitializeComponent();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            progressBar1.Visible = false;
            lblLoading.Tag = lblLoading.Text;        // stash original label text so we can restore it 
            lblOptimizing.Tag = lblOptimizing.Text;  //     when processing multiple files

            lblVersion.Text = "v" + Application.ProductVersion.ToString();

            dbConn = new SQLiteConnection("Data Source=:memory:;Version=3;");
            dbConn.Open();
        }

        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            dbConn.Close();
            dbConn.Dispose();
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            this.Refresh();

            var inFileList = new List<string>();
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++ )
            {
                inFileList.Add(args[i]);
            }
            if (inFileList.Count == 0)
            {
                using (var ofd = new OpenFileDialog())
                {
                    ofd.Title = "Select file(s) to optimize ...";
                    ofd.Filter = "Supported files (*.csv, *.ov2)|*.csv;*.ov2|CSV files (*.csv)|*.csv|OV2 files (*.ov2)|*.ov2";
                    ofd.InitialDirectory = (string)Microsoft.Win32.Registry.GetValue(hkcuKeyName, "LastOpenFolder", "");
                    ofd.Multiselect = true;
                    DialogResult dr = ofd.ShowDialog();
                    if (dr == System.Windows.Forms.DialogResult.Cancel) this.Close();
                    foreach (string fn in ofd.FileNames)
                    {
                        inFileList.Add(fn);
                        Microsoft.Win32.Registry.SetValue(hkcuKeyName, "LastOpenFolder", Path.GetDirectoryName(fn));
                    }
                }
            }
            if (inFileList.Count > 1)
            {
                if (DialogResult.No == MessageBox.Show(
                        "You have selected multiple files for processing. Please note:" + System.Environment.NewLine +
                         System.Environment.NewLine +
                         "Any selected OV2 files will be replaced with their optimized versions. Any OV2 files created from selected CSV files will be placed in the same folder as the CSV file, overwriting any existing OV2 file of that name." + System.Environment.NewLine +
                         System.Environment.NewLine +
                         "If you want to make backups of your current OV2 files you should copy those files to another location BEFORE running this utility." + System.Environment.NewLine +
                         System.Environment.NewLine +
                         "Do you want to proceed with batch processing?", Application.ProductName, MessageBoxButtons.YesNo))
                {
                    this.Close();
                    return;
                }
                    
            }

            foreach (string inFileSpec in inFileList)
            {
                lblFileName.Text = Path.GetFileName(inFileSpec);
                lblFileName.Refresh();
                lblLoading.Text = lblLoading.Tag.ToString();
                lblLoading.ForeColor = System.Drawing.Color.Black;
                lblLoading.Refresh();
                lblOptimizing.Text = lblOptimizing.Tag.ToString();
                lblOptimizing.ForeColor = System.Drawing.Color.Gray;
                lblOptimizing.Refresh();

                using (var cmd = new SQLiteCommand(dbConn))
                {
                    cmd.CommandText = "CREATE TABLE poiData (intLon INT, intLat INT, poiName VARCHAR(255), col4 VARCHAR(255))";
                    cmd.ExecuteNonQuery();
                }

                limitE = -18000000; limitW = 18000000; limitN = -9000000; limitS = 9000000;  // Note: signs reversed to represent the most unlikely values
                int recordCount = LoadDatabaseFromFile(inFileSpec);

                if (recordCount > 0)
                {
                    using (var cmd = new SQLiteCommand(dbConn))
                    {
                        cmd.CommandText = "CREATE INDEX idxLon ON poiData (intLon)";
                        cmd.ExecuteNonQuery();
                        cmd.CommandText = "CREATE INDEX idxLat ON poiData (intLat)";
                        cmd.ExecuteNonQuery();
                    }

                    var sw = new System.Diagnostics.Stopwatch();
                    sw.Start();

                    lblOptimizing.ForeColor = System.Drawing.Color.Black;
                    lblOptimizing.Refresh();
                    progressBar1.Maximum = recordCount;
                    progressBar1.Value = 0;
                    progressBar1.Visible = true;
                    processedCount = 0;

                    // process the entire rectangular space, recursing as required
                    List<byte> ov2data = ProcessBlock(limitE, limitN, limitW, limitS, 0);

                    sw.Stop();
                    progressBar1.Visible = false;
                    lblOptimizing.Text = String.Format("{0} {1:N0} records processed in {2:N1} seconds", lblOptimizing.Text, recordCount, sw.ElapsedMilliseconds / 1000.0);
                    lblOptimizing.Refresh();

                    string outFileSpec = Path.GetDirectoryName(inFileSpec);
                    if (!outFileSpec.EndsWith(@"\")) outFileSpec += @"\";
                    outFileSpec += Path.GetFileNameWithoutExtension(inFileSpec) + ".ov2";
                    if (inFileList.Count == 1)
                    {
                        bool okayToSave = false;
                        while (!okayToSave)
                        {
                            using (var sfd = new SaveFileDialog())
                            {
                                sfd.Filter = "OV2 files (*.ov2)|*.ov2";
                                sfd.InitialDirectory = (string)Microsoft.Win32.Registry.GetValue(hkcuKeyName, "LastSaveFolder", "");
                                sfd.FileName = Path.GetFileName(outFileSpec);
                                DialogResult dr = sfd.ShowDialog();
                                if (dr == System.Windows.Forms.DialogResult.Cancel)
                                {
                                    if (DialogResult.Yes == MessageBox.Show("Discard optimized file?", Application.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2))
                                    {
                                        outFileSpec = "";
                                        okayToSave = true;  // to get out of loop
                                    }
                                }
                                else
                                {
                                    outFileSpec = sfd.FileName;
                                    Microsoft.Win32.Registry.SetValue(hkcuKeyName, "LastSaveFolder", Path.GetDirectoryName(sfd.FileName));
                                    okayToSave = true;
                                }
                            }
                        }
                    }
                    this.Refresh();
                    if (outFileSpec.Length > 0)
                    {
                        try
                        {
                            File.WriteAllBytes(outFileSpec, ov2data.ToArray());
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                        }
                    }
                }
                using (var cmd = new SQLiteCommand(dbConn))
                {
                    cmd.CommandText = "DROP TABLE poiData";
                    cmd.ExecuteNonQuery();
                }
                System.Threading.Thread.Sleep(1500);
            }
            this.Close();
        }

        private List<byte> ProcessBlock(int limitE, int limitN, int limitW, int limitS, int lastSplitDirection)
        {
            Application.DoEvents();
            List<byte> subBlockBytes = new List<byte>();  // running list of data bytes for this instance and all subsequent recursions

            int maxPoiPerBlock = 20;  // TomTom normally uses a max. of 20 POI per rectangular block
            if (((limitE - limitW) == 1) || ((limitN - limitS) == 1))
            {
                maxPoiPerBlock = Int32.MaxValue - 1;  // override for 0.00001 degree strips
            }
            int splitDirection = (++lastSplitDirection) % 2;  // 0 = split N/S, 1 = split E/W

            var poiRows = new DataTable();
            using (var cmd = new SQLiteCommand(dbConn))
            {
                cmd.CommandText =
                        "SELECT intLon, intLat, poiName, col4 FROM poiData " +
                        "WHERE intLon<=@limitE AND intLon>=@limitW AND intLat<=@limitN AND intLat>=@limitS " +
                        "ORDER BY intLon LIMIT " + (maxPoiPerBlock + 1).ToString();
                cmd.Parameters.AddWithValue("@limitE", limitE);
                cmd.Parameters.AddWithValue("@limitW", limitW);
                cmd.Parameters.AddWithValue("@limitN", limitN);
                cmd.Parameters.AddWithValue("@limitS", limitS);
                using (var da = new SQLiteDataAdapter(cmd))
                {
                    da.Fill(poiRows);
                }
            }

            if (poiRows.Rows.Count > 0)
            {
                if (poiRows.Rows.Count > maxPoiPerBlock)
                {
                    // need to subdivide block and recurse
                    poiRows.Dispose();

                    int midpoint = 0;
                    if (splitDirection == 1)
                    {
                        midpoint = (limitE + limitW) / 2;
                        subBlockBytes.AddRange(ProcessBlock(midpoint, limitN, limitW, limitS, splitDirection));
                        subBlockBytes.AddRange(ProcessBlock(limitE, limitN, midpoint + 1, limitS, splitDirection));
                    }
                    else
                    {
                        midpoint = (limitN + limitS) / 2;
                        subBlockBytes.AddRange(ProcessBlock(limitE, midpoint, limitW, limitS, splitDirection));
                        subBlockBytes.AddRange(ProcessBlock(limitE, limitN, limitW, midpoint + 1, splitDirection));
                    }
                }
                else
                {
                    // add data to subBlockBytes
                    foreach (DataRow row in poiRows.Rows)
                    {
                        byte recType = 2;
                        string stringData = row["poiName"].ToString() + "\0";
                        if (!DBNull.Value.Equals(row["col4"]))
                        {
                            recType = 3;
                            stringData += row["col4"].ToString() + "\0";
                        }
                        subBlockBytes.Add(recType);
                        subBlockBytes.AddRange(BitConverter.GetBytes(stringData.Length + 13));
                        subBlockBytes.AddRange(BitConverter.GetBytes(Convert.ToInt32(row["intLon"])));
                        subBlockBytes.AddRange(BitConverter.GetBytes(Convert.ToInt32(row["intLat"])));
                        Encoding enc = Encoding.GetEncoding("iso-8859-1");
                        subBlockBytes.AddRange(enc.GetBytes(stringData));
                        processedCount++;
                    }
                    poiRows.Dispose();
                }
                List<byte> skipperBytes = new List<byte>();
                skipperBytes.Add(1);  // record type 1: skipper record
                skipperBytes.AddRange(BitConverter.GetBytes(subBlockBytes.Count + 21));
                skipperBytes.AddRange(BitConverter.GetBytes(limitE));
                skipperBytes.AddRange(BitConverter.GetBytes(limitN));
                skipperBytes.AddRange(BitConverter.GetBytes(limitW));
                skipperBytes.AddRange(BitConverter.GetBytes(limitS));
                subBlockBytes.InsertRange(0, skipperBytes);  // glue skipper record to the beginning
            }

            progressBar1.Value = processedCount;
            
            return subBlockBytes;
        }

        private int LoadDatabaseFromFile(string inFileSpec)
        {
            const int statusUpdateInterval = 100;
            int recordCount = 0;
            var sw = new System.Diagnostics.Stopwatch();
            string labelBaseText = lblLoading.Text;
            sw.Start();

            using (var cmd = new SQLiteCommand(dbConn))
            {
                cmd.CommandText = "INSERT INTO poiData (intLon, intLat, poiName, col4) VALUES (@intLon, @intLat, @poiName, @col4)";
                cmd.Parameters.Add("@intLon", DbType.Int32);
                cmd.Parameters.Add("@intLat", DbType.Int32);
                cmd.Parameters.Add("@poiName", DbType.AnsiString);
                cmd.Parameters.Add("@col4", DbType.AnsiString);
                cmd.Prepare();

                string ext = Path.GetExtension(inFileSpec).ToUpper();
                if (ext == ".CSV")
                {
                    using (var parser = new TextFieldParser(inFileSpec, Encoding.GetEncoding("iso-8859-1")))
                    {
                        parser.TextFieldType = FieldType.Delimited;
                        parser.SetDelimiters(",");
                        while (!parser.EndOfData)
                        {
                            //Process row
                            string[] fields = parser.ReadFields();
                            int intLon = 18100000;
                            try
                            {
                                intLon = Convert.ToInt32(Math.Round(Convert.ToDouble(fields[0]) * 100000));
                            }
                            catch { }
                            int intLat = 9100000;
                            try
                            {
                                intLat = Convert.ToInt32(Math.Round(Convert.ToDouble(fields[1]) * 100000));
                            }
                            catch { }
                            string poiName = "";
                            try
                            {
                                poiName = fields[2].ToString();
                            }
                            catch { }
                            string col4 = null;
                            if (fields.Length > 3)
                            {
                                col4 += fields[3].ToString() + "\0";
                                try
                                {
                                    col4 += fields[4].ToString();
                                }
                                catch { }
                            }
                            if (ValidatePoiData(intLon, intLat, poiName))
                            {
                                cmd.Parameters["@intLon"].Value = intLon;
                                cmd.Parameters["@intLat"].Value = intLat;
                                cmd.Parameters["@poiName"].Value = poiName;
                                cmd.Parameters["@col4"].Value = col4;
                                cmd.ExecuteNonQuery();
                                if ((++recordCount % statusUpdateInterval) == 0)
                                {
                                    lblLoading.Text = String.Format("{0} {1:N0} records processed", labelBaseText, recordCount);
                                    lblLoading.Refresh();
                                }
                            }
                        }
                        parser.Close();
                    }
                }
                else  // not CSV, assume OV2
                {
                    try
                    {
                        using (var fs = new FileStream(inFileSpec, FileMode.Open, FileAccess.Read))
                        {
                            using (var br = new BinaryReader(fs, new ASCIIEncoding()))
                            {
                                while (br.BaseStream.Position < br.BaseStream.Length)
                                {
                                    byte recType = br.ReadByte();
                                    int bytesToSkip = 0;
                                    switch (recType)
                                    {
                                        case 0:  // deleted POI record
                                            int recLength = br.ReadInt32();
                                            bytesToSkip = recLength - 5;
                                            break;
                                        case 1:  // skipper record
                                            bytesToSkip = 20;
                                            break;
                                        case 2:
                                        case 3:
                                            // do nothing
                                            break;
                                        default:
                                            MessageBox.Show(
                                                String.Format("Unknown record type {0} encountered. This application cannot optimize the file. (The file is either encrypted or corrupt.)", recType),
                                                Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
                                            return 0;
                                    }
                                    if (bytesToSkip > 0)
                                    {
                                        br.ReadBytes(bytesToSkip);
                                    }
                                    else
                                    {
                                        int recLength = BitConverter.ToInt32(br.ReadBytes(4), 0);
                                        int intLon = br.ReadInt32();
                                        int intLat = br.ReadInt32();
                                        string s = Encoding.GetEncoding("iso-8859-1").GetString(br.ReadBytes(recLength - 14));
                                        br.ReadByte();  // skip null terminator

                                        string poiName = "";
                                        string col4 = null;
                                        if (recType == 2)
                                        {
                                            poiName = s;
                                        }
                                        else  // type 3
                                        {
                                            string[] split = null;
                                            split = s.Split(new char[1] { '\0' }, 2);
                                            poiName = split[0];
                                            if (split.Length > 1) col4 = split[1];
                                        }
                                        if (ValidatePoiData(intLon, intLat, poiName))
                                        {
                                            cmd.Parameters["@intLon"].Value = intLon;
                                            cmd.Parameters["@intLat"].Value = intLat;
                                            cmd.Parameters["@poiName"].Value = poiName;
                                            cmd.Parameters["@col4"].Value = col4;
                                            cmd.ExecuteNonQuery();
                                            if ((++recordCount % statusUpdateInterval) == 0)
                                            {
                                                lblLoading.Text = String.Format("{0} {1:N0} records processed", labelBaseText, recordCount);
                                                lblLoading.Refresh();
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (EndOfStreamException)
                    {
                        MessageBox.Show("The current input file is damaged. It cannot be processed.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 0;
                    }
                    catch (IOException ex)
                    {
                        MessageBox.Show(ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return 0;
                    }
                }
            }
            sw.Stop();
            lblLoading.Text = String.Format("{0} {1:N0} records processed in {2:N1} seconds", labelBaseText, recordCount, sw.ElapsedMilliseconds / 1000.0);
            lblLoading.Refresh();
            return recordCount;
        }

        private bool ValidatePoiData(int intLon, int intLat, string poiName)
        {
            bool isValid = true;
            if ((intLon < -18000000) || (intLon > 18000000) || (intLat < -9000000) || (intLat > 9000000) || (poiName.Length == 0))
            {
                isValid = false;
            }
            else
            {
                // keep track of max/min values as we go
                if (intLon > limitE) limitE = intLon;
                if (intLon < limitW) limitW = intLon;
                if (intLat > limitN) limitN = intLat;
                if (intLat < limitS) limitS = intLat;
            }

            return isValid;
        }
    }
}
