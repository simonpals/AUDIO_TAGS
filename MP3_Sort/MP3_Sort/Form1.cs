using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;
using System.Linq;

namespace MP3_Sort
{
    public partial class Form1 : Form
    {
        Dictionary<string, string> config = new Dictionary<string, string>();

        FolderBrowserDialog openFileDialog = new FolderBrowserDialog();
        FolderBrowserDialog saveFileDialog = new FolderBrowserDialog();

        List<System.Diagnostics.Process> asyncProcessList = new List<System.Diagnostics.Process>();

        public struct AlbumKey
        {
            public string name;
            public string label;
        }

        public Form1()
        {
            InitializeComponent();
        }

        private Bitmap ResizeImage(Image image, int width, int height)
        {
            var destRect = new Rectangle(0, 0, width, height);
            var destImage = new Bitmap(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (var graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (var wrapMode = new ImageAttributes())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        private void LoadConfig()
        {
            try
            {
                string line;
                StreamReader file = new StreamReader("config.ini");

                while ((line = file.ReadLine()) != null)
                {
                    string[] param = line.Split('=');

                    if (param.Length < 2)
                        continue;

                    config[param[0]] = param[1];
                }

                file.Close();
            }
            catch (Exception)
            {
                return;
            }
        }

        private void SaveConfig()
        {
            try
            {
                StreamWriter file = new StreamWriter("config.ini");

                foreach (KeyValuePair<string, string> param in config)
                {
                    file.WriteLine(param.Key + "=" + param.Value);
                }

                file.Close();
            }
            catch (Exception)
            {
                return;
            }
        }

        private string NormalizeTagValue(string str)
        {
            string normStr = str;

            //normStr = normStr.Replace("&", "and");
            normStr = normStr.Replace('\\', ',');
            normStr = normStr.Replace('/', ',');
            normStr = normStr.Replace(':', ' ');
            normStr = normStr.Replace('*', ' ');
            normStr = normStr.Replace('?', ' ');
            normStr = normStr.Replace('"', ' ');
            normStr = normStr.Replace('>', ' ');
            normStr = normStr.Replace('<', ' ');
            normStr = normStr.Replace('|', ' ');

            return normStr;
        }

        private Dictionary<string, int> ReadFolderHistory()
        {
            Dictionary<string, int> dic = new Dictionary<string, int>();

            try
            {
                StreamReader fileStream = new StreamReader("folders.txt");
                string line;

                while ((line = fileStream.ReadLine()) != null)
                    dic[line] = 1;

                fileStream.Close();
            }
            catch (Exception) { }

            return dic;
        }

        private void SaveFolderHistory(Dictionary<string, int> dic)
        {
            try
            {
                StreamWriter fileStream = new StreamWriter("folders.txt");

                foreach (string key in dic.Keys)
                    fileStream.WriteLine(key);

                fileStream.Close();
            }
            catch (Exception) { }
        }

        private void ExecuteCommandAsync(string command, string args)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo procStartInfo = new System.Diagnostics.ProcessStartInfo(command, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                System.Diagnostics.Process proc = new System.Diagnostics.Process
                {
                    StartInfo = procStartInfo
                };

                proc.Start();
                asyncProcessList.Add(proc);
            }
            catch (Exception) { }
        }

        private void WaitExecutedCommandsFinish()
        {
            foreach(var proc in asyncProcessList)
            {
                proc.WaitForExit();
            }
        }

        private void ClearExecuteCommandList()
        {
            asyncProcessList.Clear();
        }

        private void StartSorting()
        {
            DirectoryInfo dir;
            bool folderFail = false;

            try
            {
                dir = new DirectoryInfo(textBox1.Text);
            }
            catch (Exception)
            {
                MessageBox.Show("Указанная папка не найдена!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var allowedExts = new string[] { "*.mp3", "*.flac", "*.aiff" };
            FileInfo[] allFiles = allowedExts.SelectMany(i => dir.GetFiles(i)).ToArray();

            if (allFiles.Length == 0)
            {
                MessageBox.Show("В указанной папке треки не найдены!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            
            ClearExecuteCommandList();

            this.Invoke((MethodInvoker)delegate
            {
                Enabled = false;
            });

            Dictionary<AlbumKey, Dictionary<string, int>> albums_artists = new Dictionary<AlbumKey, Dictionary<string, int>>();
            //Dictionary<AlbumKey, Id3.Frames.PictureFrame> albums_pictures = new Dictionary<AlbumKey, Id3.Frames.PictureFrame>();
            Dictionary<AlbumKey, ATL.PictureInfo> albums_pictures = new Dictionary<AlbumKey, ATL.PictureInfo>();
            Dictionary<AlbumKey, string> albums_folders = new Dictionary<AlbumKey, string>();

            this.Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = 0;
                progressBar1.Maximum = allFiles.Length;
                label3.Text = "Получение информации об аудио-тегах...";
            });

            foreach (FileInfo file in allFiles)
            {
                //using (var mp3 = new Id3.Mp3(file.FullName))
                var track = new ATL.Track(file.FullName);
                //{
                //Id3.Id3Tag tag = mp3.GetTag(Id3.Id3TagFamily.Version2X);
                string publisher = (track.Publisher.Any() ? track.Publisher : (track.AdditionalFields.ContainsKey("ORGANIZATION") ? track.AdditionalFields["ORGANIZATION"] : ""));
                //if (tag.Album.Value == null || tag.Artists.Value.Count < 1 || tag.Pictures.Count < 1 || tag.Publisher.Value == null)
                if (!track.Album.Any() || !track.Artist.Any() || track.EmbeddedPictures.Count < 1 || !publisher.Any())
                    continue;

                AlbumKey album_key = new AlbumKey
                {
                    //label = tag.Publisher.Value.ToString(),
                    //name = tag.Album.Value.ToString()
                    label = publisher,
                    name = track.Album
                };

                if (!albums_artists.ContainsKey(album_key))
                {
                    albums_artists[album_key] = new Dictionary<string, int>();
                    //albums_pictures[album_key] = tag.Pictures[0];
                    albums_pictures[album_key] = track.EmbeddedPictures[0];

                    //if (checkBox5.Checked)
                    //{
                    //    Image img = Image.FromStream(new MemoryStream(albums_pictures[album_key].PictureData));
                    //    Bitmap bitImg = ResizeImage(img, 500, 500);
                    //    MemoryStream ms = new MemoryStream();
                    //    bitImg.Save(ms, ImageFormat.Jpeg);
                    //    albums_pictures[album_key].PictureData = ms.ToArray();
                    //}
                }

                if (!albums_artists[album_key].ContainsKey(track.Artist))
                    albums_artists[album_key][track.Artist] = 1;
                else
                    albums_artists[album_key][track.Artist] += 1;

                //foreach (string artist in tag.Artists.Value)
                //{
                //if (!albums_artists[album_key].ContainsKey(artist))
                //    albums_artists[album_key][artist] = 1;
                //else
                //    albums_artists[album_key][artist] += 1;
                //}

                this.Invoke((MethodInvoker)delegate
                {
                    progressBar1.Value += 1;
                });
                //}
            }

            this.Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = progressBar1.Maximum;
            });

            Dictionary<string, int> albums_history = ReadFolderHistory();

            foreach (KeyValuePair<AlbumKey, Dictionary<string, int>> album in albums_artists)
            {
                string folderName = "";
                int artist_cnt = 0;
                string folderFullName = "";

                foreach (KeyValuePair<string, int> artist in albums_artists[album.Key])
                {
                    if (artist_cnt > 1)
                        break;

                    if (artist_cnt > 0)
                        folderFullName += ", ";

                    folderFullName += artist.Key;
                    ++artist_cnt;
                }

                folderFullName += " - ";

                folderName += album.Key.name;

                if (checkBox2.Checked)
                    folderName += " [" + album.Key.label + "]";

                folderName = NormalizeTagValue(folderName);
                folderFullName += folderName;
                folderFullName = NormalizeTagValue(folderFullName);

                try
                {
                    if (!albums_history.ContainsKey(folderName))
                    {
                        albums_folders[album.Key] = textBox2.Text + "\\" + folderFullName;
                        Directory.CreateDirectory(albums_folders[album.Key]);

                        if (checkBox8.Checked)
                            albums_history[folderName] = 1;
                    }
                }
                catch (Exception)
                {
                    folderFail = true;
                }

                if (checkBox6.Checked && textBox3.Text.Any())
                {
                    try
                    {
                        File.Copy(textBox3.Text, textBox2.Text + "\\" + folderFullName + "\\" + Path.GetFileName(textBox3.Text), true);
                    }
                    catch (Exception) { }
                }
            }

            this.Invoke((MethodInvoker)delegate
            {
                progressBar1.Value = 0;
                label3.Text = "Сортировка треков...";
            });

            bool sameDisk = Path.GetPathRoot(textBox1.Text).Equals(Path.GetPathRoot(textBox2.Text));

            List<string> watermarkedFolder = new List<string>();

            foreach (FileInfo file in allFiles)
            {
                AlbumKey album_key;
                //Id3.Id3Tag tag;
                string targetFileName;

                //using (var mp3 = new Id3.Mp3(file.FullName))
                //{
                //tag = mp3.GetTag(Id3.Id3TagFamily.Version2X);
                var track = new ATL.Track(file.FullName);

                string publisher = (track.Publisher.Any() ? track.Publisher : (track.AdditionalFields.ContainsKey("ORGANIZATION") ? track.AdditionalFields["ORGANIZATION"] : ""));
                //if (tag.Album.Value == null || tag.Artists.Value.Count < 1 || tag.Pictures.Count < 1 || tag.Publisher.Value == null)
                if (!track.Album.Any() || !track.Artist.Any() || track.EmbeddedPictures.Count < 1 || !publisher.Any())
                    continue;

                album_key = new AlbumKey
                {
                    //label = tag.Publisher.Value.ToString(),
                    //name = tag.Album.Value.ToString()
                    label = publisher,
                    name = track.Album
                };

                //}

                if (!albums_folders.ContainsKey(album_key))
                    continue;

                //if (checkBox3.Checked && !file.Name.Contains("[" + tag.Publisher.Value.ToString() + "]"))
                if (checkBox3.Checked && !file.Name.Contains("[" + publisher + "]"))
                    //targetFileName = albums_folders[album_key] + "\\" + file.Name.Substring(0, file.Name.Length - 4) + " [" + NormalizeTagValue(tag.Publisher.Value.ToString()) + "].mp3";
                    targetFileName = albums_folders[album_key] + "\\" + Path.GetFileNameWithoutExtension(file.Name) + " [" + NormalizeTagValue(publisher) + "]" + file.Extension;
                else
                    targetFileName = albums_folders[album_key] + "\\" + file.Name;

                try
                {
                    if (checkBox7.Checked)
                    {
                        if (sameDisk)
                            File.Move(file.FullName, targetFileName);
                        else
                        {
                            File.Copy(file.FullName, targetFileName, true);
                            ExecuteCommandAsync("cmd", "/c del /F \"" + file.FullName + "\"");
                        }
                    }
                    else
                    {
                        File.Copy(file.FullName, targetFileName, true);
                    }
                }
                catch (Exception) { }

                string arg = "\"" + targetFileName + "\"";
                bool hasWatermark = false;

                if (checkBox9.Checked)
                {
                    if (radioButton1.Checked || (radioButton2.Checked && !watermarkedFolder.Contains(albums_folders[album_key])))
                    {
                        arg += " " + "\"" + textBox4.Text + "\"";
                        watermarkedFolder.Add(albums_folders[album_key]);
                        hasWatermark = true;
                    }
                }

                if (checkBox5.Checked)
                {
                    ExecuteCommandAsync("resize", arg);
                }
                else if (hasWatermark)
                {
                    arg += " " + "\"" + "watermarkonly" + "\"";
                    ExecuteCommandAsync("resize", arg);
                }

                try
                {
                    if (checkBox4.Checked && !File.Exists(albums_folders[album_key] + "\\" + NormalizeTagValue(album_key.name) + ".jpg"))
                    {
                        //albums_pictures[album_key].SaveImage(albums_folders[album_key] + "\\" + NormalizeTagValue(album_key.name) + ".jpg");

                        byte[] rawPictureData = albums_pictures[album_key].PictureData;

                        if (checkBox5.Checked)
                        {
                            Image img = Image.FromStream(new MemoryStream(rawPictureData));
                            Bitmap bitImg = ResizeImage(img, 500, 500);
                            MemoryStream ms = new MemoryStream();
                            bitImg.Save(ms, ImageFormat.Jpeg);
                            rawPictureData = ms.ToArray();
                        }

                        Image.FromStream(new MemoryStream(rawPictureData)).Save(albums_folders[album_key] + "\\" + NormalizeTagValue(album_key.name) + ".jpg");
                        if (checkBox9.Checked)
                        {
                            string args = "\"" + albums_folders[album_key] + "\\" + NormalizeTagValue(album_key.name) + ".jpg" + "\"";
                            args += " " + "\"" + textBox4.Text + "\"";
                            arg += " " + "\"" + "watermarkonly" + "\"";

                            ExecuteCommandAsync("resize", args);
                        }
                    }
                }
                catch (Exception) { }

                this.Invoke((MethodInvoker)delegate
                {
                    progressBar1.Value += 1;
                });

            }

            //Wait to finish resize
            WaitExecutedCommandsFinish();
            //Thread.Sleep(2000);

            if (checkBox1.Checked)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    progressBar1.Value = 0;
                    progressBar1.Maximum = albums_folders.Keys.Count;
                    label3.Text = "Создание архивов...";
                });

                foreach (KeyValuePair<AlbumKey, string> album in albums_folders)
                {
                    try
                    {
                        if (Directory.Exists(album.Value))
                        {
                            ZipFile.CreateFromDirectory(album.Value, album.Value + ".zip", CompressionLevel.Optimal, true);
                            ExecuteCommandAsync("cmd", "/c rmdir /Q /S \"" + album.Value + "\"");
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "Ошибка при создании архива", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }

                    this.Invoke((MethodInvoker)delegate
                    {
                        progressBar1.Value += 1;
                    });
                }
            }

            SaveFolderHistory(albums_history);

            this.Invoke((MethodInvoker)delegate
            {
                label3.Text = "Готово";
                progressBar1.Value = progressBar1.Maximum;
            });

            MessageBox.Show("Готово!", "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (folderFail)
                MessageBox.Show("Не удалось создать папки для некоторых альбомов!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);

            this.Invoke((MethodInvoker)delegate
            {
                label3.Text = "";
                progressBar1.Value = 0;
                this.Enabled = true;
            });
        }


        private void button3_Click(object sender, EventArgs e)
        {
            Task.Factory.StartNew(() => { StartSorting(); });
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.Cancel)
                return;

            textBox1.Text = openFileDialog.SelectedPath;
            config["source_path"] = openFileDialog.SelectedPath;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (saveFileDialog.ShowDialog() == DialogResult.Cancel)
                return;

            textBox2.Text = saveFileDialog.SelectedPath;
            config["destination_path"] = saveFileDialog.SelectedPath;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            LoadConfig();
            ApplyConfig();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
        }

        private void button4_Click(object sender, EventArgs e)
        {
            OpenFileDialog selectFileDialog = new OpenFileDialog
            {
                Multiselect = false
            };

            if (selectFileDialog.ShowDialog() == DialogResult.Cancel)
                return;

            textBox3.Text = selectFileDialog.FileName;
            config["info_file"] = selectFileDialog.FileName;
        }

        private void ApplyConfig()
        {
            bool b;

            if (config.ContainsKey("source_path"))
                textBox1.Text = config["source_path"];

            if (config.ContainsKey("destination_path"))
                textBox2.Text = config["destination_path"];

            if (config.ContainsKey("info_file"))
                textBox3.Text = config["info_file"];

            if (config.ContainsKey("watermark_file"))
                textBox4.Text = config["watermark_file"];

            if (config.ContainsKey("make_archive"))
                if (bool.TryParse(config["make_archive"], out b))
                    checkBox1.Checked = b;

            if (config.ContainsKey("watermark_file_mode"))
                if (bool.TryParse(config["watermark_file_mode"], out b))
                {
                    radioButton1.Checked = b;
                    radioButton2.Checked = !b;
                }

            if (config.ContainsKey("include_label_folders"))
                if (bool.TryParse(config["include_label_folders"], out b))
                    checkBox2.Checked = b;

            if (config.ContainsKey("include_label_files"))
                if (bool.TryParse(config["include_label_files"], out b))
                    checkBox3.Checked = b;

            if (config.ContainsKey("save_album_images"))
                if (bool.TryParse(config["save_album_images"], out b))
                    checkBox4.Checked = b;

            if (config.ContainsKey("resize_album_images"))
                if (bool.TryParse(config["resize_album_images"], out b))
                    checkBox5.Checked = b;

            if (config.ContainsKey("add_info_file"))
                if (bool.TryParse(config["add_info_file"], out b))
                    checkBox6.Checked = b;

            if (config.ContainsKey("add_watermark_file"))
                if (bool.TryParse(config["add_watermark_file"], out b))
                    checkBox9.Checked = b;

            if (config.ContainsKey("delete_source_files"))
                if (bool.TryParse(config["delete_source_files"], out b))
                    checkBox7.Checked = b;

            if (config.ContainsKey("save_folders"))
                if (bool.TryParse(config["save_folders"], out b))
                    checkBox8.Checked = b;
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            config["make_archive"] = checkBox1.Checked.ToString();
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            config["include_label_folders"] = checkBox2.Checked.ToString();
        }

        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            config["include_label_files"] = checkBox3.Checked.ToString();
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            config["save_album_images"] = checkBox4.Checked.ToString();
        }

        private void checkBox5_CheckedChanged(object sender, EventArgs e)
        {
            config["resize_album_images"] = checkBox5.Checked.ToString();
        }

        private void checkBox6_CheckedChanged(object sender, EventArgs e)
        {
            config["add_info_file"] = checkBox6.Checked.ToString();
            textBox3.Enabled = checkBox6.Checked;
            button4.Enabled = checkBox6.Checked;
        }

        private void checkBox7_CheckedChanged(object sender, EventArgs e)
        {
            config["delete_source_files"] = checkBox7.Checked.ToString();
        }

        private void checkBox8_CheckedChanged(object sender, EventArgs e)
        {
            config["save_folders"] = checkBox8.Checked.ToString();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            try
            {
                File.Delete("folders.txt");
            }
            catch
            {
                MessageBox.Show("Не удалось очистить список созданных альбомов!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkBox9_CheckedChanged(object sender, EventArgs e)
        {
            config["add_watermark_file"] = checkBox9.Checked.ToString();
            textBox4.Enabled = checkBox9.Checked;
            button6.Enabled = checkBox9.Checked;
            radioButton1.Enabled = checkBox9.Checked;
            radioButton2.Enabled = checkBox9.Checked;
        }

        private void button6_Click(object sender, EventArgs e)
        {
            OpenFileDialog selectFileDialog = new OpenFileDialog
            {
                Multiselect = false
            };

            if (selectFileDialog.ShowDialog() == DialogResult.Cancel)
                return;

            textBox4.Text = selectFileDialog.FileName;
            config["watermark_file"] = selectFileDialog.FileName;
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            config["watermark_file_mode"] = radioButton1.Checked.ToString();
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            config["watermark_file_mode"] = radioButton1.Checked.ToString();
        }
    }
}
