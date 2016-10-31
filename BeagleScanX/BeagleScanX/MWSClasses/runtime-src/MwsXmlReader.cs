/******************************************************************************* 
 * Copyright 2009-2012 Amazon Services. All Rights Reserved.
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 *
 * You may not use this file except in compliance with the License. 
 * You may obtain a copy of the License at: http://aws.amazon.com/apache2.0
 * This file is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR 
 * CONDITIONS OF ANY KIND, either express or implied. See the License for the 
 * specific language governing permissions and limitations under the License.
 *******************************************************************************
 * Marketplace Web Service Runtime Client Library
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using Windows.Data.Xml.Dom;
using Xamarin.Forms;



namespace MWSClientCsRuntime
{

    public class MwsXmlReader : IMwsReader
    {

        private XDocument doc;
        private XElement currentElement;
        private XNode currentChild;

        //readonly static TypeCode[] numericTypes = { TypeCode.Double, TypeCode.Int16, TypeCode.Int32, TypeCode.Int64 };

        /// <summary>
        /// Load the xml file and initialize <code>current</code> and <code>currentChild</code>
        /// </summary>
        /// <param name="xmlValue">xml content as string</param>
        public MwsXmlReader(string xmlValue)
        {
            doc = new XDocument(xmlValue);
            //doc.LoadXml(xmlValue);
            //initialize current node to root
            currentElement = doc.Root;
            currentChild = currentElement.FirstNode;
            
        }

        // methods
        public T Read<T>(string name)
        {
            T value = default(T);
            XNode startChild = currentChild;
            if (doc.Root.DescendantNodes().Count() == 0)
            {
                return value;
            }
            do
            {

                if (doc.Root.FirstNode.Equals(name))
                {
                    if (currentChild is XAttribute)
                    {
                        value = ConvertValue<T>(currentChild.ToString());
                    }
                    else
                    {
                        value = ReadObject<T>(currentChild);
                    }
                    currentChild = XElement.Parse(doc.Root.NextNode.ToString());
                    if (currentChild == null)
                        currentChild = XElement.Parse(doc.Root.FirstNode.ToString());
                    if (currentChild == startChild)
                        currentChild = XElement.Parse(doc.Root.NextNode.ToString()) != null ? startChild.NextNode : currentElement.FirstNode;
                    return value;
                }
                currentChild = XElement.Parse(doc.Root.NextNode.ToString());
                if (currentChild == null)
                    currentChild = currentElement.FirstNode;
            } while (currentChild != startChild);
            currentChild = startChild.NextNode != null ? startChild.NextNode : currentElement.FirstNode;
            return value;
        }

        public List<T> ReadList<T>(string name)
        {
            List<T> list = new List<T>();
            XNode startChild = currentChild;
            if (currentElement.DescendantNodes().Count() == 0)
            {
                return null;
            }
            do
            {
                if (currentChild.Equals(name) && currentChild is XElement)
                {
                    list.Add(ReadObject<T>(currentChild));
                }
                currentChild = currentChild.NextNode;
                if (currentChild == null)
                    currentChild = currentElement.FirstNode;
            } while (currentChild != startChild);
            currentChild = startChild.NextNode != null ? startChild.NextNode : currentElement.FirstNode;
            return list;
        }

        public List<T> ReadList<T>(string name, string memberName)
        {
            List<T> list = new List<T>();
            XNode startChild = currentChild;
            if (currentElement.DescendantNodes().Count() == 0)
            {
                return null;
            }
            do
            {
                if (currentChild.Equals(name) && currentChild is XElement)
                {
                    BeginObject(currentChild);
                    List<T> innerList = ReadList<T>(memberName);
                    if (innerList!=null) {
                        list.AddRange(innerList);
                    }
                    EndObject();
                }
                currentChild = currentChild.NextNode;
                if (currentChild == null)
                    currentChild = currentElement.FirstNode;
            } while (currentChild != startChild);
            currentChild = startChild.NextNode != null ? startChild.NextNode : currentElement.FirstNode;
            return list;
        }

        public List<XElement> ReadAny()
        {
            List<XElement> elements = new List<XElement>();
            foreach (XNode node in currentElement.DescendantNodes())
            {
                if (node.NodeType == XmlNodeType.Element)
                {
                    elements.Add(XmlExtensions.ToXElement(node));
                }
            }
            return elements;
        }

        public T ReadAttribute<T>(string name)
        {
            XAttribute node = currentElement.Attributes(name).FirstOrDefault();
            if (node != null && node.Value != null)
            {
                return ConvertValue<T>(node.Value);
            }
            else
            {
                throw new Exception("Attribute \'" + name + "\' does not exist");
            }
        }

        public T ReadValue<T>()
        {
            if (currentChild == null || currentChild.NodeType != XmlNodeType.Text)
            {
                throw new Exception("Cannot read current value");
            }
            else
            {
                return ConvertValue<T>(currentChild.ToString());
            }
        }

        private void BeginObject(XNode node)
        {
            if (node == null)
                throw new Exception("Cannot read null node");
            //move to child node for complex object reading
            currentElement = XmlExtensions.ToXElement(node);
            currentChild = XmlExtensions.ToXElement(node).FirstNode;
           
        }

        private void EndObject()
        {
            //move the child pointer back to the parent node (BeginObject should have been called)
            currentChild = currentElement;
            currentElement = currentElement.Parent;
        }

       private T ConvertValue<T>(string str)
        {
            Type type = typeof(T);

            if (type == typeof(string))
            {
                return (T)(object)str;
            }
            else if (type == typeof(decimal)) 
            {
                return (T)(object)convertToDecimal(str);
            }
            else if(typeof(Enum).GetType().GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                return (T)MwsUtil.GetEnumValue(type, str);
            }
            else if (typeof(IComparable).GetType().GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                return (T)Convert.ChangeType(str, type, CultureInfo.InvariantCulture);
            } 
            else if(Nullable.GetUnderlyingType(type) != null)
            {
                return convertNullableType<T>(str);
            }
            else
            {
                throw new InvalidDataException("Unsupported type for conversion from string: " + type.FullName);
            }
        }
        
       private decimal convertToDecimal(string str) {
             return Decimal.Parse(str,
                    System.Globalization.NumberStyles.Float, 
                    CultureInfo.InvariantCulture);
        }

        private T convertNullableType<T>(string str) 
        {
            // Nullable types don't pass through Convert correctly
            if(String.IsNullOrEmpty(str))
            {
                return default(T);
            }
            else 
            {
                Type type = typeof(T);

                // First, get the type that is nullable
                Type valueType = Nullable.GetUnderlyingType(type);
                // Recurse using the actual value type: ConvertValue<valueType> (must use reflection since we are generic)
                //MethodInfo recursiveMethod = this.GetType().GetMethod("ConvertValue", BindingFlags.NonPublic | BindingFlags.Instance);
                //var value = recursiveMethod.MakeGenericMethod(valueType).Invoke(this, new object[] { str });
                // Return a new Nullable<valueType> (which is T) using the value we converted
                return (T) Activator.CreateInstance(type, new object[] { str });
            }
        }
        
        /// <summary>
        /// Read element value
        /// </summary>
        /// <param name="t">Expected type of the element</param>
        /// <param name="node">XNode to be read from</param>
        /// <returns>Value of element</returns>
        private T ReadObject<T>(XNode node)
        {
            Type type = typeof(T);

            if (typeof(IMwsObject).GetType().GetTypeInfo().IsAssignableFrom(type.GetTypeInfo()))
            {
                T mwsObject = MwsUtil.NewInstance<T>();
                BeginObject(node);
                (mwsObject as IMwsObject).ReadFragmentFrom(this);
                EndObject();
                return mwsObject;
            }
            else if (type == typeof(object))
            {
                return (T) (object) node;
            }
            else
            {
                return ConvertValue<T>(XmlExtensions.XmlInnerText(XmlExtensions.ToXElement(node)));
            }
        }
              

        //private bool IsNumberType(Type type)
        //{
        //    return numericTypes.Contains(Type.GetTypeCode(type));
        //}        
    }

    public static class XmlExtensions
    {
        public static XElement ToXElement(this XNode node)
        {
            XDocument xDoc = new XDocument();
            using (XmlWriter xmlWriter = xDoc.CreateWriter())
                node.WriteTo(xmlWriter);
            return xDoc.Root;
        }

        public static XNode ToXNode(this XElement element)
        {
            using (XmlReader xmlReader = element.CreateReader())
            {
                XDocument xmlDoc = new XDocument(xmlReader);                
                return xmlDoc;
            }
        }
        public static String XmlInnerText(this XElement node)
        {

            XmlReader xReader = node.CreateReader();
            xReader.MoveToContent();
            return xReader.ReadInnerXml();            
        }
    }
}


