﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.Xml;
using EBop.MapObjects.MapInfo;
using System.Threading;
using System.Collections;
using ICSharpCode.SharpZipLib;


namespace OSM2TAB
{
    public partial class Form1 : Form
    {
        public class NodeInfo
        {
            public double lat;
            public double lon;
            public SortedList tags;
        }

        public class TagInfo
        {
            public string k;
            public string v;
        }

        public class WayInfo
        {
            public int id;
            public bool corrupt = false;
            public ArrayList nodes;
            public SortedList tags;
        }

        // Storage of max Tag Key string length
        public class TagKeyLength
        {
            public int max;
            public string k;
        }

        private Thread m_myThread;
        static int m_maxWays; // Cross-thread
        static string m_status; // Cross-thread
        static string m_debug; // Cross-thread
        static bool m_goButtonEnabled = true; // Cross-thread
        static Form1 m_myForm;
        const int m_cFieldSize = 64;

        public delegate void delegateSetMaxWays();
        public delegate void delegateSetCurrentWay();
        public delegate void delegateSetDebug();
        public delegate void delegateGoButtonEnabled();

        
        public delegateSetMaxWays m_myMaxWaysDelegate;
        public delegateSetCurrentWay m_myCurrentWayDelegate;
        public delegateSetDebug m_myDebugDelegate;
        public delegateGoButtonEnabled m_myGoButtonEnabledDelegate;

        public void setMaxWays()
        {
            labelMaxWays.Text = m_maxWays.ToString();
        }
        public void setCurrentWay()
        {
            labelCurrentWay.Text = m_status;
        }
        public void setDebug()
        {
            debugTextBox.Text = m_debug;
        }
        public void setGoButtonEnabled()
        {
            buttonGo.Enabled = m_goButtonEnabled;
        }

        // http://www.openstreetmap.org/api/0.6/map?bbox=-1.080351,50.895238,-1.054516,50.907688
        private void workerThread()
        {
            // Disable 'Go' button until processing is done
            m_goButtonEnabled = false;
            m_myForm.Invoke(m_myForm.m_myGoButtonEnabledDelegate);

            // Load Theme data
            ArrayList keys = new ArrayList();
            //ArrayList allWayTags = new ArrayList(); // All keys in all ways

            XmlDocument themeDoc = new XmlDocument();
            themeDoc.Load(themeTextBox.Text);
            // Load key list
            XmlNodeList keyNodeList = themeDoc.SelectNodes("osm2tab/keys/key");

            foreach (XmlNode keyNode in keyNodeList)
            {
                keys.Add(keyNode.SelectSingleNode("@name").Value);
            }
            
            double[] pointsX = new double[2000]; // 2000 is 0.6 OSM spec
            double[] pointsY = new double[2000];

            // Optimal settings?
            // 1. Don't add features that don't match 'style key's in the theme
            // 2. <next criteria of optimal>
            // 3. <next criteria of optimal>
                // Regions
                bool optimalRegions = false;
                XmlNode regionStylesNode = themeDoc.SelectSingleNode("osm2tab/regionStyles");
                XmlNode optimalRegionStylesNode = regionStylesNode.SelectSingleNode("@optimal");
                if(optimalRegionStylesNode != null)
                    optimalRegions = optimalRegionStylesNode.Value == "yes";
                // Lines
                bool optimalLines = false;
                XmlNode lineStylesNode = themeDoc.SelectSingleNode("osm2tab/lineStyles");
                XmlNode optimalLineStylesNode = lineStylesNode.SelectSingleNode("@optimal");
                if (optimalLineStylesNode != null)
                    optimalLines = optimalLineStylesNode.Value == "yes";

            // Load OSM data
            System.IO.FileStream bz2Stream = System.IO.File.OpenRead(inputTextBox.Text);
            ICSharpCode.SharpZipLib.BZip2.BZip2InputStream osmStream = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(bz2Stream);

            XmlTextReader reader = new XmlTextReader(osmStream);

            SortedList nodeList = new SortedList();

            SortedList wayList = new SortedList();

            SortedList tagKeyLengthList = new SortedList();

            WayInfo currentWay = null;

            NodeInfo currentNode = null;

           
            m_status = "Starting";
            m_myForm.Invoke(m_myForm.m_myCurrentWayDelegate);

            // Used to calculate TAB database field format
            //needs to Be an Array of longest strings per field otherwise all fields will Get same width. even simple ones
            //but field widths should be consistent so what do we do
            int longestString = 0;

            int loadedNodeCount = 0;
            // Cache XML file as fast-readable database in ram
            while (reader.Read())
            {
                // Log progress
                if (loadedNodeCount++ % 100000 == 0)
                {
                    m_status = "Loading OSM: " + Convert.ToString(loadedNodeCount/100000);
                    m_myForm.Invoke(m_myForm.m_myCurrentWayDelegate);
                }

                switch (reader.NodeType)
                {
                    case System.Xml.XmlNodeType.Element:
                    {
                        if (reader.Name.Equals("node"))
                        {
                            //currentWay = null; // tags can be for nodes or ways
                            int id = Convert.ToInt32(reader.GetAttribute("id"));

                            NodeInfo node = new NodeInfo();
                            node.tags = new SortedList();
                           

                            node.lat = Convert.ToDouble(reader.GetAttribute("lat"));
                            node.lon = Convert.ToDouble(reader.GetAttribute("lon"));
                            if(!nodeList.Contains(id))
                                 nodeList.Add(id, node);
                            else
                            {
                                // need to trace data source error
                            }

                            currentWay = null;
                            currentNode = node;
                        }
                        else if (reader.Name.Equals("way"))
                        {  
                            WayInfo way = new WayInfo();
                            way.nodes = new ArrayList();
                            way.tags = new SortedList();

                            way.id = Convert.ToInt32(reader.GetAttribute("id"));
                            // this can be a duplicate in an error in the osm data
                            // therefore current node should be null and nd and way needs to check for null

                            if (!wayList.Contains(way.id))
                                wayList.Add(way.id, way);
                            else
                            {
                                // !! need to trace error
                                currentWay.corrupt = true;
                            }

                            currentWay = way;
                            currentNode = null;
                        }
                        else if (reader.Name.Equals("nd"))
                        {
                            int ndRef = Convert.ToInt32(reader.GetAttribute("ref"));
                            NodeInfo ni = (NodeInfo)nodeList[ndRef];
                            
                            if (ni != null)
                            {
                                currentWay.nodes.Add(ni);
                            }
                            else
                            {
                                // Node ref not found - error!
                                currentWay.corrupt = true;
                            }
                            
                        }
                        else if (reader.Name.Equals("tag"))
                        {
                            // Way Tags
                            //if (currentWay != null)
                            //{
                                // Way Keys only
                                TagInfo ki = new TagInfo();
                                ki.k = reader.GetAttribute("k");
                                ki.v = reader.GetAttribute("v");

                                if (currentWay != null)
                                {
                                   currentWay.tags.Add(ki.k, ki);
                                }

                                if (currentNode != null)
                                {
                                   currentNode.tags.Add(ki.k, ki);
                                }

                                // Compile list of tags for TAB fields if not specified in theme.xml
                                if ((keyNodeList.Count == 0) && !keys.Contains(ki.k))
                                    keys.Add(ki.k);

                                // Update max key name size for MI tab field width
                                // Each Key
                                if (!tagKeyLengthList.Contains(ki.k))
                                {
                                    // If there is not a 
                                    TagKeyLength tkl = new TagKeyLength();
                                    tkl.k = ki.k;
                                    tkl.max = ki.v.Length;
                                    tagKeyLengthList.Add(ki.k, tkl);
                                }
                                else
                                {
                                    TagKeyLength tkl = (TagKeyLength)tagKeyLengthList[ki.k];
                                    if (tkl.max < ki.v.Length)
                                        tkl.max = ki.v.Length;
                                }   
                                
                                // log longest string
                                if (ki.v.Length > longestString)
                                    longestString = ki.v.Length;
                            //}
                            //else if (currentRelation blah blah != null)
                            //{
                            //}
                        }
                        else if (reader.Name.Equals("relation"))
                        {
                            currentWay = null;
                            currentNode = null;
                            int a = 0;
                            int b = a + 1;
                        }
                        break;
                    }
                }
            }

            // Create MapInfo tabs
            IntPtr regionTabFile = MiApi.mitab_c_create(outputTextBox.Text + "\\" + tabPrefix.Text + "_region.tab", "tab", "Earth Projection 1, 104", 0, 0, 0, 0);
            IntPtr lineTabFile = MiApi.mitab_c_create(outputTextBox.Text + "\\" + tabPrefix.Text + "_line.tab", "tab", "Earth Projection 1, 104", 0, 0, 0, 0);
            IntPtr pointTabFile = MiApi.mitab_c_create(outputTextBox.Text + "\\" + tabPrefix.Text + "_point.tab", "tab", "Earth Projection 1, 104", 0, 0, 0, 0);

            // Create fields
            int index = 0;

            // Max MapInfo fields is 255 (not 256 it seems)
            if (keys.Count > 250)
            {
                m_debug += "Too many fields";
                m_myForm.Invoke(m_myForm.m_myDebugDelegate);

                int amountToReduce = keys.Count - 250;
                keys.RemoveRange(250, amountToReduce);
            }

            foreach (string key in keys)
            {
                // Get max field width
                TagKeyLength tkl = (TagKeyLength)tagKeyLengthList[key];

                if (tkl != null)
                {
                    MiApi.mitab_c_add_field(regionTabFile, key, 1, tkl.max, 0, 0, 0);
                    MiApi.mitab_c_add_field(lineTabFile, key, 1, tkl.max, 0, 0, 0);
                    MiApi.mitab_c_add_field(pointTabFile, key, 1, tkl.max, 0, 0, 0);
                }
                else
                {
                    // No key exists in the dataset so there is no max length - make it 32
                    MiApi.mitab_c_add_field(regionTabFile, key, 1, 32, 0, 0, 0);
                    MiApi.mitab_c_add_field(lineTabFile, key, 1, 32, 0, 0, 0);
                    MiApi.mitab_c_add_field(pointTabFile, key, 1, 32, 0, 0, 0);
                }

                index++;
            }

            // Create MapInfo tabs from cached database
            m_maxWays = wayList.Count;
            m_myForm.Invoke(m_myForm.m_myMaxWaysDelegate);

            // Points
            for (int i = 0; i < nodeList.Count; i++)
            {
               NodeInfo node = (NodeInfo)nodeList.GetByIndex(i);

               // If a node has tags then it's a point object.
               // Not entirely convinced this is the best way to deduce point objects.
               if (node.tags.Count >= 1)
               {


               }
            }
  
            // Lines and Regions
            for (int i = 0; i < wayList.Count; i++)
            {
                // Log progress
                if (i % 10000 == 0)
                {
                    m_status = "Translating : " + Convert.ToString((100* i) / wayList.Count) + "%";
                    m_myForm.Invoke(m_myForm.m_myCurrentWayDelegate);
                }

                WayInfo way = (WayInfo)wayList.GetByIndex(i);
                if (way.corrupt)
                    continue;

                bool iAmARegion = false;
                ArrayList nodes = ((WayInfo)wayList.GetByIndex(i)).nodes;

                // Is this a region? i.e. first and last nodes the same
                NodeInfo firstNode = (NodeInfo)nodes[0];
                NodeInfo lastNode = (NodeInfo)nodes[nodes.Count - 1];
                if ((firstNode.lat == lastNode.lat)
                    && (firstNode.lon == lastNode.lon))
                {
                    iAmARegion = true;
                }

                for (int j = 0; j < nodes.Count; j++)
                {
                    NodeInfo node = (NodeInfo)nodes[j];
                    pointsX[j] = node.lon;
                    pointsY[j] = node.lat;   
                }

                if (iAmARegion && nodes.Count >=3)
                {
                    // Region
                    IntPtr feat = MiApi.mitab_c_create_feature(regionTabFile, 7); // 7 = region
                    // 'part' param is -1 for single part regions. Need to use relation nodes to add 'holes' to -1 part polys which have poly numbers 1+ 
                    MiApi.mitab_c_set_points(feat, -1, nodes.Count - 1, pointsX, pointsY); // nodes.Count -1 as last and first nodes are the same
                    int gti = 0; // get tag info
                    foreach (string key in keys)
                    {
                        TagInfo tag = (TagInfo)way.tags[key];
                        if (tag != null) // Not every field is used
                            MiApi.mitab_c_set_field(feat, gti, tag.v);

                        gti++;
                    }

                    // Set Region Style
                    XmlNodeList styleNodeList = themeDoc.SelectNodes("osm2tab/regionStyles/style");
                    bool styleFoundForThisFeature = false; // Used in conjunction with 'optimal' region option

                    foreach (XmlNode styleNode in styleNodeList)
                    {
                        string styleKey = styleNode.SelectSingleNode("@key").Value;
                        TagInfo tag = (TagInfo)way.tags[styleKey];
                        if (tag != null) // Not every field is used
                        {
                            XmlNode valueAttribute = styleNode.SelectSingleNode("@value");
                            string styleKeyValue = "";
                            if (valueAttribute != null)
                                styleKeyValue = valueAttribute.Value;
                            // If there is no value attribute for this key then all features with this key have style applied
                            if (tag.v == styleKeyValue || valueAttribute == null)
                            {
                                int pattern = Convert.ToInt32(styleNode.SelectSingleNode("@pattern").Value);
                                int foreground = Convert.ToInt32(styleNode.SelectSingleNode("@foreground").Value);
                                int background = Convert.ToInt32(styleNode.SelectSingleNode("@background").Value);
                                int transparent = Convert.ToInt32(styleNode.SelectSingleNode("@transparent").Value);

                                int penPattern = Convert.ToInt32(styleNode.SelectSingleNode("@penPattern").Value);
                                int penColour = Convert.ToInt32(styleNode.SelectSingleNode("@penColour").Value);
                                int penWidth = Convert.ToInt32(styleNode.SelectSingleNode("@penWidth").Value);

                                MiApi.mitab_c_set_brush(feat, foreground, background, pattern, transparent);
                                MiApi.mitab_c_set_pen(feat, penWidth, penPattern, penColour);

                                styleFoundForThisFeature = true;
                            }
                        }
                    }

                    if (!(!styleFoundForThisFeature && optimalRegions) && // Only write feature to TAB if style is specified
                        way.tags.Count != 0) // Don't write feature if it has no tags. TODO make this optional in Theme xml file
                    {
                        MiApi.mitab_c_write_feature(regionTabFile, feat);
                    }

                    MiApi.mitab_c_destroy_feature(feat);
                }
                else if (nodes.Count >= 2)
                {
                    // Line
                    IntPtr feat = MiApi.mitab_c_create_feature(regionTabFile, 5); // 5 = region
                    MiApi.mitab_c_set_points(feat, 0, nodes.Count, pointsX, pointsY); // part is 0 - can we use relation nodes to associate further (>0) parts?
                    int gti = 0; // get tag info
                    foreach (string key in keys)
                    {
                        TagInfo tag = (TagInfo)way.tags[key];
                        if(tag != null) // Not every field is used
                            MiApi.mitab_c_set_field(feat, gti, tag.v);

                        gti++;
                    }

                    // Set Line Style
                    XmlNodeList styleNodeList = themeDoc.SelectNodes("osm2tab/lineStyles/style");

                    bool styleFoundForThisFeature = false; // Used in conjunction with 'optimal' line option
                    foreach (XmlNode styleNode in styleNodeList)
                    {
                        string styleKey = styleNode.SelectSingleNode("@key").Value;
                        TagInfo tag = (TagInfo)way.tags[styleKey];
                        if (tag != null) // Not every field is used
                        {
                            XmlNode valueAttribute = styleNode.SelectSingleNode("@value");
                            string styleKeyValue = "";
                            if (valueAttribute != null)
                                styleKeyValue = valueAttribute.Value;
                            // If there is no value attribute for this key then all features with this key have style applied
                            if (tag.v == styleKeyValue || valueAttribute == null)
                            {
                                int penPattern = Convert.ToInt32(styleNode.SelectSingleNode("@penPattern").Value);
                                int penColour = Convert.ToInt32(styleNode.SelectSingleNode("@penColour").Value);
                                int penWidth = Convert.ToInt32(styleNode.SelectSingleNode("@penWidth").Value);

                                MiApi.mitab_c_set_pen(feat, penWidth, penPattern, penColour);

                                styleFoundForThisFeature = true;
                            }
                        }
                    }

                    if (!(!styleFoundForThisFeature && optimalLines) && // Only write feature to TAB if style is specified
                        way.tags.Count != 0) // Don't write feature if it has no tags. TODO make this optional in Theme xml file
                    {
                        MiApi.mitab_c_write_feature(lineTabFile, feat);
                    }

                    MiApi.mitab_c_destroy_feature(feat);
                }
                
            }

            MiApi.mitab_c_close(regionTabFile);
            MiApi.mitab_c_close(lineTabFile);

            m_status = "Done!";
            //m_status = longestString.ToString();
            m_myForm.Invoke(m_myForm.m_myCurrentWayDelegate);

            // Enable 'Go' button
            m_goButtonEnabled = true;
            m_myForm.Invoke(m_myForm.m_myGoButtonEnabledDelegate);
        }

        public Form1()
        {
            InitializeComponent();

            m_myMaxWaysDelegate = new delegateSetMaxWays(setMaxWays);
            m_myCurrentWayDelegate = new delegateSetCurrentWay(setCurrentWay);
            m_myDebugDelegate = new delegateSetDebug(setDebug);
            m_myGoButtonEnabledDelegate = new delegateGoButtonEnabled(setGoButtonEnabled);
            m_myForm = this;
        }

        private void buttonTestThread_Click(object sender, EventArgs e)
        {
            bool okToProcess = true;

            if (outputTextBox.Text == "")
            {
                MessageBox.Show("Please enter a folder in the 'Output' box above.\r\rYou can use the button at the right side of this box to select a folder.", "There is a problem");
                okToProcess = false;
            }

            if (inputTextBox.Text == "")
            {
                MessageBox.Show("Please enter a file or URL in the 'Input' box above.\r\rYou can use the button at the right side of this box to select an OSM file.", "There is a problem");
                okToProcess = false;
            }

            if(okToProcess)
            {
                m_myThread = new Thread(new ThreadStart(workerThread));
                m_myThread.Start();
            }
        }

        private void outputFolderBrowserDialog1_HelpRequest(object sender, EventArgs e)
        {

        }

        private void buttonSelectOutFolder_Click(object sender, EventArgs e)
        {
            outputFolderBrowserDialog.Description = "Output folder for TAB files";
            outputFolderBrowserDialog.ShowDialog();
            outputTextBox.Text = outputFolderBrowserDialog.SelectedPath;
        }

        private void buttonSelectInFile_Click(object sender, EventArgs e)
        {
            openOSMFileDialog.FileName = "";
            openOSMFileDialog.Multiselect = false;
            openOSMFileDialog.Title = "Select OSM file to translate";
            openOSMFileDialog.DefaultExt = "osm";
            openOSMFileDialog.CheckPathExists = true;
            openOSMFileDialog.CheckFileExists = true;
            openOSMFileDialog.Filter = "OpenStreetMap files (*.bz2)|*.bz2|All files (*.*)|*.*";
            openOSMFileDialog.ShowDialog();

            inputTextBox.Text = openOSMFileDialog.FileName;
        }
    }
}
