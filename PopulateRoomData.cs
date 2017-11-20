using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitServices.Persistence;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Proto = Revit.Elements;
using DS = Autodesk.DesignScript.Geometry;
using System.Text;
using System.Globalization;
using Revit.GeometryConversion;

namespace DynamoAecom
{
    [Transaction(TransactionMode.Manual)]
    public class DesigntechViews
    {
        internal DesigntechViews()
        {

        }
        /// <summary>
        /// Sort Views on Sheets by Size
        /// </summary>
        /// <param name="views"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public static List<List<View>> SortViewportsBySize([DefaultArgument("{}")] IList<IList> views,
            [DefaultArgument("{}")] int width,
            [DefaultArgument("{}")] int height)
        {
            int iterations = views.Count;
            double feet = 304.8;
            int wi = width;
            int he = height;

            using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Re-arranging Views", "Processing {0} out of " + iterations.ToString() + " elements", iterations))
            {
                RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                List<List<View>> outlines = new List<List<View>>();

                foreach (var el in views)
                {
                    List<View> view_set = new List<View>();

                    if (form.getAbortFlag())
                    {
                        return null;
                    }

                    foreach (var e in el)
                    {
                        View v = e as ViewSection;

                        double w = (v.Outline.Max.U - v.Outline.Min.U) * feet;
                        double h = (v.Outline.Max.V - v.Outline.Min.V) * feet;

                        Parameter view_comment = v.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);

                        if ((w < wi / 2) && (h < he / 2))
                        {
                            view_comment.Set("A");
                        }
                        else if ((w > wi / 2) && (w < wi) && (he < h / 4))
                        {
                            view_comment.Set("B");
                        }
                        else if ((w > wi / 2) && (w < wi) && (he < h / 2))
                        {
                            view_comment.Set("C");
                        }
                        else if ((w < wi) && (h < he))
                        {
                            view_comment.Set("D");
                        }
                        else
                        {
                            view_comment.Set("E");
                        }

                        view_set.Add(v);
                    }

                    form.Increment();

                    outlines.Add(view_set);

                    RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
                }

                return outlines;
            }
        }
        #region TagElements
        [MultiReturn(new[] { "views", "points", "elements" })]
        public static Dictionary<string, object> ViewElementIntersect([DefaultArgument("{}")] IList elementGeometries,
            [DefaultArgument("{}")] IList<string> elementsNames,
            [DefaultArgument("{}")] IList<string> elementsText,
            [DefaultArgument("{}")] IList<DS.Solid> viewGeometries,
            [DefaultArgument("{}")] IList<string> viewNames,
            [DefaultArgument("{}")] IList viewSheets
            )
        {
            int numElements = elementGeometries.Count;
            int numSections = viewGeometries.Count;

            List<object> vNames = new List<object>();
            List<DS.Point> points = new List<DS.Point>();
            List<string> eNames = new List<string>();

            if (numElements != elementsNames.Count || numSections != viewNames.Count)
            {
                TaskDialog.Show("Error", "Make sure you are passing matching lists.");
                return null;
            }

            using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Find elements on Views.", "Processing {0} out of " + (numElements * numSections).ToString() + " elements", (numElements * numSections)))
            {
                RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                // FOR EACH VIEW
                for (int i = 0; i < numSections; i++)
                {
                    string viewName = viewNames[i];
                    DS.Solid viewSolid = viewGeometries[i];
                    List<DS.Point> sectionPoints = new List<DS.Point>();

                    // FOR EACH ELEMENT
                    for (int j = 0; j < numElements; j++)
                    {
                        form.Increment();
                        if (form.getAbortFlag())
                        {
                            return null;
                        }
                        // 1 - check if the solid is not null
                        if (viewSolid == null)
                        {
                            continue;
                        }
                        bool test = false;
                        // 2 - check if element is in the room at all
                        if (elementsNames[j] != null && elementsNames[j].Contains(viewName))
                        {
                            test = true;
                        }

                        if (!test)
                        {
                            continue;
                        }

                        DS.Cuboid elementCube = elementGeometries[j] as DS.Cuboid;
                        // 3 - check if the cuboid is not null
                        if (elementCube == null)
                        {
                            continue;
                        }

                        IList<DS.Geometry> geometry = viewSolid.Intersect(elementCube);

                        if (geometry.Count > 0)
                        {
                            try
                            {
                                DS.Point point = GetPoint(geometry);
                                if (point == null)
                                {
                                    continue;
                                }
                                //point = CheckPoint(sectionPoints, point);
                                //sectionPoints.Add(point);
                                vNames.Add(viewSheets[i]);
                                points.Add(point);
                                eNames.Add(elementsText[j]);
                            }
                            catch(Exception ex)
                            {
                                TaskDialog.Show("Error", ex.Message + ex.Data.ToString());
                            }
                        }
                    }
                }

                RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
            }

            return new Dictionary<string, object>
                {
                    { "views", vNames },
                    { "points", points },
                    { "elements", eNames }
                };
        }

        private static DS.Point CheckPoint(List<DS.Point> sectionPoints, DS.Point point)
        {
            DS.Point result = null;

            foreach(DS.Point p in sectionPoints)
            {
                if(p.X == point.X && p.Y == point.Y)
                {
                    result = CheckPoint(sectionPoints, DS.Point.ByCoordinates(point.X, point.Y + 2.0, point.Z));
                }
                else
                {
                    result = point;
                }
            }
            return result;
        }

        /// <summary>
        /// Retrieves the center point from a DS.Geometry
        /// </summary>
        /// <param name="geometry"></param>
        /// <returns></returns>
        private static DS.Point GetPoint(IList<DS.Geometry> geometry)
        {
            if (geometry[0].ToString().Contains("Line"))
            {
                DS.Curve curve = geometry[0] as DS.Curve;
                if(curve != null)
                {
                    return curve.PointAtParameter(0.5);
                }
                else return null;
            }
            else if (geometry[0].ToString().Contains("Solid"))
            {
                DS.Solid solid = geometry[0] as DS.Solid;
                if (solid != null)
                {
                    return solid.Centroid();
                }
                else return null;
            }
            else
            {
                return null;
            }
        }
        #endregion

        #region Views.PopulateRoomInformation 
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rooms"></param>
        /// <param name="points"></param>
        /// <param name="elements"></param>
        /// <param name="parameter"></param>
        /// <returns></returns>
        [MultiReturn(new[] {  "points" })]
        public static Dictionary<string, object> PopulateRoomInformation([DefaultArgument("{}")] IList rooms,
            [DefaultArgument("{}")] IList<IList> points,
            [DefaultArgument("{}")] IList elements,
            [DefaultArgument("{}")] string parameter)
        {
            List<object> pts = new List<object>();
            int iterations = rooms.Count * elements.Count;
            int iteration = 0;

            using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Clear parameter value.", "Processing {0} out of " + elements.Count.ToString() + " elements", elements.Count))
            {
                RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                foreach (var el in elements)
                {
                    Element element = ((Proto.Element)el).InternalElement;
                    element.LookupParameter(parameter).Set("");
                }

                form.Increment();
                iteration++;

                RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
            }


            using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Populating Room Data", "Processing {0} out of " + iterations.ToString() + " elements", iterations))
            {
                iteration = 0;
                RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                for (int i = 0; i < elements.Count; i++)
                {
                    object pointCarry = null;

                    Element el = ((Proto.Element)elements[i]).InternalElement;
                    Parameter param = el.LookupParameter(parameter);
                    if (param == null)
                    {
                        continue;
                    }
                    foreach (var room in rooms)
                    {
                        if (form.getAbortFlag())
                        {
                            return null;
                            //return "Aborted by user";
                        }

                        Autodesk.Revit.DB.Architecture.Room r = ((Proto.Element)room).InternalElement as Autodesk.Revit.DB.Architecture.Room;

                        object testPoint = AssignRoom(r, el, points[i], param);

                        if(testPoint != null)
                        {
                            pointCarry = (testPoint as XYZ);
                        }

                        form.Increment();
                        iteration++;
                    }
                    if (param.AsString().Equals(""))
                    {
                        param.Set("n/a");
                    }
                    pts.Add(pointCarry);
                }

                RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
            }
            return new Dictionary<string, object>
                {
                    { "points", pts }
                };
            //return String.Format("{0} elements processed successfully.", iterations.ToString());
        }
        private static object AssignRoom(Autodesk.Revit.DB.Architecture.Room room, DS.Solid solid, Element element, IList points, Parameter parameter)
        {
            foreach (var point in points)
            {
                DS.Point p = point as DS.Point;
                if (p == null) continue;
                if (solid.DoesIntersect(p))
                {
                    string name = room.LookupParameter("Number").AsString() + " - " + room.LookupParameter("Name").AsString();
                    if (!parameter.AsString().Equals(""))
                    {
                        name += ", " + parameter.AsString();
                    }
                    parameter.Set(name);
                    return point;
                }
            }
            return null;
        }
        private static object AssignRoom(Autodesk.Revit.DB.Architecture.Room room, Element element, IList points, Parameter parameter)
        {
            foreach (var point in points)
            {
                if (room.IsPointInRoom(point as XYZ))
                {
                    string name = room.LookupParameter("Number").AsString() + " - " + room.LookupParameter("Name").AsString();
                    if (!parameter.AsString().Equals(""))
                    {
                        name += ", " + parameter.AsString();
                    }
                    parameter.Set(name);
                    return point;
                }
            }
            return null;
        }
        /// <summary>
        /// Feeds room information to element in a chosen parameter
        /// </summary>
        /// <param name="rooms"></param>
        /// <param name="solids"></param>
        /// <param name="points"></param>
        /// <param name="elements"></param>
        /// <param name="parameter"></param>
        /// <returns></returns>
        [MultiReturn(new[] { "points" , "message" })]
        public static Dictionary<string, object> PopulateRoomInformationBySolids([DefaultArgument("{}")] IList rooms,
            [DefaultArgument("{}")] IList solids,
            [DefaultArgument("{}")] IList<IList> points,
            [DefaultArgument("{}")] IList elements,
            [DefaultArgument("{}")] string parameter)
        {
            List<object> pts = new List<object>();
            int iterations = rooms.Count * elements.Count;
            int iteration = 0;
            int success = 0;

            if (rooms.Count != solids.Count)
            {
                TaskDialog.Show("Error", "Room count not equal to solid count.");
                return null;
            }

            if(points.Count != elements.Count)
            {
                TaskDialog.Show("Error", "Elements count not equal to points count.");
                return null;
            }

            try
            {
                // Prep the elements
                // Delete the parameter value
                using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Clear parameter value.", "Processing {0} out of " + elements.Count.ToString() + " elements", elements.Count))
                {
                    RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                    foreach (var el in elements)
                    {
                        Element element = ((Proto.Element)el).InternalElement;
                        if (element == null) continue;
                        element.LookupParameter(parameter).Set("");
                    }

                    form.Increment();
                    iteration++;

                    RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
                }
                // Populate element parameters
                using (AdnRme.ProgressForm form = new AdnRme.ProgressForm("Populating Room Data", "Processing {0} out of " + iterations.ToString() + " elements", iterations))
                {
                    iteration = 0;
                    RevitServices.Transactions.TransactionManager.Instance.EnsureInTransaction(DocumentManager.Instance.CurrentUIDocument.Document);

                    for (int i = 0; i < elements.Count; i++)
                    {
                        object pointCarry = null;

                        Element el = ((Proto.Element)elements[i]).InternalElement;
                        if (el == null) continue;
                        Parameter param = el.LookupParameter(parameter);
                        if (param == null)
                        {
                            continue;
                        }
                        for (int j = 0; j < rooms.Count; j++)
                        {
                            if (form.getAbortFlag())
                            {
                                return null;
                                //return "Aborted by user";
                            }

                            Autodesk.Revit.DB.Architecture.Room r = ((Proto.Element)rooms[j]).InternalElement as Autodesk.Revit.DB.Architecture.Room;
                            DS.Solid s = (DS.Solid)solids[j];
                            if (r == null || s == null || points[i] == null) continue;
                            object testPoint = AssignRoom(r, s, el, points[i], param);

                            if (testPoint != null)
                            {
                                pointCarry = testPoint;
                            }

                            form.Increment();
                            iteration++;

                        }
                        if (param.AsString().Equals(""))
                        {
                            param.Set("n/a");
                        }
                        if (pointCarry != null) success++;
                        pts.Add(pointCarry);
                    }

                    RevitServices.Transactions.TransactionManager.Instance.TransactionTaskDone();
                }
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message + ex.Data.ToString());
            }
            string message = String.Format("{0} out of {1} elements are contained within rooms and were successfully processed.", success.ToString(), elements.Count.ToString());
            return new Dictionary<string, object>
            {
                { "points", pts },
                { "message", message }
            };
        }

        #endregion
    }


    public class DesigntechString
    {
        internal DesigntechString()
        {

        }

        #region String.AddLeadingZeros
        /// <summary>
        /// Takes an input of a series of numbers, finds the length of the maximum number and then adds leading zero’s to any numbers of less digits till all numbers in the list have the same digits.
        /// 
        /// As an example if you feed in the numbers 1, 10 and 1000, it will give you the results 0001, 0010, 1000. Lists can be in any order as it searches for the maximum item.
        /// </summary>
        /// <param name="numbers"></param>
        /// <returns name="numbers">string</returns>
        /// <search>string,add,leading,zeros,0</search>
        public static List<string> AddLeadingZeros(List<int> numbers)
        {
            List<int> lengths = new List<int>();
            foreach (int i in numbers)
            {
                string str = (System.Math.Abs(i)).ToString();
                int len = str.Length;
                lengths.Add(len);
            }
            int max = lengths.Max();

            List<string> output = new List<string>();
            foreach (int i in numbers)
            {
                string format = "D" + max.ToString();
                output.Add(i.ToString(format));
            }
            return output;
        }
        #endregion

        #region String.ContainsThisAndThat
        /// <summary>
        /// Returns true or false based on whether the string contains all the inputs (mulitple input as a list)
        /// </summary>
        /// <param name="str"></param>
        /// <param name="searchFor"></param>
        /// <param name="ignoreCase"></param>
        /// <returns name="str">string</returns>
        /// <search>string,contains,this,and,that,case,search</search>
        public static object ContainsThisAndThat(string str, List<string> searchFor, bool ignoreCase = false)
        {
            int count = searchFor.Count();
            var boolList = new List<bool>();
            for (int i = 0; i < count; i++)
            {
                //bool contains = str.Contains(searchFor[i]);
                bool contains = DSCore.String.Contains(str, searchFor[i], ignoreCase);
                boolList.Add(contains);
            }
            bool alltrue = boolList.TrueForAll(b => b);
            return alltrue;
        }
        #endregion

        #region String.ContainsThisOrThat
        /// <summary>
        /// Returns true or false based on whether the string contains any of the inputs (mulitple input as a list)
        /// </summary>
        /// <param name="str"></param>
        /// <param name="searchFor"></param>
        /// <param name="ignoreCase"></param>
        /// <returns name="str">string</returns>
        /// <search>string,contains,this,or,that,case,search</search>
        public static object ContainsThisOrThat(string str, List<string> searchFor, bool ignoreCase = false)
        {
            int count = searchFor.Count();
            var boolList = new List<bool>();
            for (int i = 0; i < count; i++)
            {
                //bool contains = str.Contains(searchFor[i]);
                bool contains = DSCore.String.Contains(str, searchFor[i], ignoreCase);
                boolList.Add(contains);
            }
            bool anyTrue = boolList.Any(b => b);
            return anyTrue;
        }
        #endregion
    }
    
}
