using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

using System.Globalization;

using AForge.Video;
using AForge.Video.DirectShow;


namespace NeuralNetwork1
{

	public delegate void FormUpdater(double progress, double error, TimeSpan time);

    public delegate void UpdateTLGMessages(string msg);

    public partial class Form1 : Form
    {
        string pathToImg = @"C:\work\xxx\IS\RecognStudents\images\";
        static int symbolsCount = 10;

        TLGBotik tlgBot;

        public BaseNetwork Net
        {
            get
            {
                var selectedItem = (string)netTypeBox.SelectedItem;
                if (!networksCache.ContainsKey(selectedItem))
                    networksCache.Add(selectedItem, CreateNetwork(selectedItem));

                return networksCache[selectedItem];
            }
        }

        private readonly Dictionary<string, Func<int[], BaseNetwork>> networksFabric;
        private Dictionary<string, BaseNetwork> networksCache = new Dictionary<string, BaseNetwork>();

        public Form1(Dictionary<string, Func<int[], BaseNetwork>> networksFabric)
        {
            InitializeComponent();
            this.networksFabric = networksFabric;
            netTypeBox.Items.AddRange(this.networksFabric.Keys.Select(s => (object)s).ToArray());
            netTypeBox.SelectedIndex = 0;
            tlgBot = new TLGBotik(Net, new UpdateTLGMessages(UpdateTLGInfo), new AIMLService());

            button3_Click(this, null);
        }

        public void UpdateLearningInfo(double progress, double error, TimeSpan elapsedTime)
		{
			if (progressBar1.InvokeRequired)
			{
				progressBar1.Invoke(new FormUpdater(UpdateLearningInfo),new Object[] {progress, error, elapsedTime});
				return;
			}
            StatusLabel.Text = "Accuracy: " + error.ToString();
            int prgs = (int)Math.Round(progress*100);
			prgs = Math.Min(100, Math.Max(0,prgs));
            elapsedTimeLabel.Text = "Затраченное время : " + elapsedTime.Duration().ToString(@"hh\:mm\:ss\:ff");
            progressBar1.Value = prgs;
		}

        public void UpdateTLGInfo(string message)
        {
            if (TLGUsersMessages.InvokeRequired)
            {
                TLGUsersMessages.Invoke(new UpdateTLGMessages(UpdateTLGInfo), new Object[] { message });
                return;
            }
            TLGUsersMessages.Text += message + Environment.NewLine;
        }

        private async Task<double> train_networkAsync(int epoches, double acceptable_error, bool parallel = true)
        {
            //  Выключаем всё ненужное
            label1.Text = "Выполняется обучение...";
            label1.ForeColor = Color.Red;
            groupBox1.Enabled = false;
            pictureBox1.Enabled = false;
            trainOneButton.Enabled = false;

            //  Создаём новую обучающую выборку
            SamplesSet samples = new SamplesSet();
            Sample newSample;

            // берем датасет из папочек
            foreach (var directory in Directory.GetDirectories(pathToImg))
            {
                int type = -1;
                switch (directory.ToString())
                {
                    case @"C:\work\xxx\IS\RecognStudents\images\play":
                        type = 0;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\pause":
                        type = 1;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\repeat":
                        type = 2;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\next":
                        type = 3;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\previous":
                        type = 4;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\louder":
                        type = 5;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\quieter":
                        type = 6;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\rewindf":
                        type = 7;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\rewindb":
                        type = 8;
                        break;
                    case @"C:\work\xxx\IS\RecognStudents\images\mix":
                        type = 9;
                        break;
                    default:
                        MessageBox.Show("Нет такой папки(((" + directory.ToString(), "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        break;
                }
                foreach (var file in Directory.GetFiles(directory))
                {
                    var img = AForge.Imaging.UnmanagedImage.FromManagedImage(new Bitmap(file));
                    newSample = new Sample(imgToData(img), symbolsCount, (FigureType)type);
                    samples.AddSample(newSample);
                }
            }
            try
            {
                var curNet = Net;
                double f = await Task.Run(() => curNet.TrainOnDataSet(samples, epoches, acceptable_error, parallel));
                groupBox1.Enabled = true;
                pictureBox1.Enabled = true;

                tlgBot = new TLGBotik(curNet, new UpdateTLGMessages(UpdateTLGInfo), new AIMLService());

                return f;
            }
            catch (Exception e)
            {
                label1.Text = $"Исключение: {e.Message}";
            }
            return 0;
        }

        private double[] imgToData(AForge.Imaging.UnmanagedImage img)
        {
            double[] res = new double[img.Width * img.Height];
            for (int i = 0; i < img.Width; i++)
            {
                for (int j = 0; j < img.Height; j++)
                {
                    //GetBrightness Возвращает значение освещенности (оттенок-насыщенность-освещенность (HSL)) для данной структуры
                    res[i * img.Width + j] = img.GetPixel(i, j).GetBrightness(); // maybe threshold
                }
            }
            return res;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            #pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            train_networkAsync((int)EpochesCounter.Value, (100 - AccuracyCounter.Value) / 100.0, parallelCheckBox.Checked);
            #pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        }

        private void button3_Click(object sender, EventArgs e)
        {
            int[] structure = CurrentNetworkStructure();
            foreach (var network in networksCache.Values)
                network.TrainProgress -= UpdateLearningInfo;
            // Пересоздаём все сети с новой структурой
            networksCache = networksCache.ToDictionary(oldNet => oldNet.Key, oldNet => CreateNetwork(oldNet.Key));
        }

        private BaseNetwork CreateNetwork(string networkName)
        {
            var network = networksFabric[networkName](CurrentNetworkStructure());
            network.TrainProgress += UpdateLearningInfo;
            return network;
        }

        private int[] CurrentNetworkStructure()
        {
            return netStructureBox.Text.Split(';').Select(int.Parse).ToArray();
        }

        private void netTrainButton_MouseEnter(object sender, EventArgs e)
        {
            infoStatusLabel.Text = "Обучить нейросеть с указанными параметрами";
        }

        private void testNetButton_MouseEnter(object sender, EventArgs e)
        {
            infoStatusLabel.Text = "Тестировать нейросеть на тестовой выборке такого же размера";
        }

        private void netTypeBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            //net = AccordNet;
        }

        private void recreateNetButton_MouseEnter(object sender, EventArgs e)
        {
            infoStatusLabel.Text = "Заново пересоздаёт сеть с указанными параметрами";
        }

        private void TLGBotOnButton_Click(object sender, EventArgs e)
        {
            tlgBot.Act();
            TLGBotOnButton.Enabled = false;
        }
    }
  }
