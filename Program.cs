using MiddleweightReflection;
using System;
using System.Reflection.Metadata;
using System.Linq;
using System.Collections.Generic;

using CustomAttributes = System.Collections.Immutable.ImmutableArray<MiddleweightReflection.MrCustomAttribute>;
using System.Xml;
using System.IO;
using System.Diagnostics;
using System.Collections.Immutable;

namespace DumpWinMD
{
    class StringCustomAttributeProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            if (typeCode == PrimitiveTypeCode.String)
            {
                return "string";
            }
            throw new Exception();
        }

        public string GetSystemType()
        {
            return "System.String";
        }

        public string GetSZArrayType(string elementType)
        {
            throw new NotImplementedException();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            throw new NotImplementedException();
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            
            throw new NotImplementedException();
        }

        public string GetTypeFromSerializedName(string name)
        {
            throw new NotImplementedException();
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            throw new NotImplementedException();
        }

        public bool IsSystemType(string type)
        {
            throw new NotImplementedException();
        }
    }
    class Program
    {
        string GetContentFromAttribute(CustomAttribute val)
        {
            var args = val.DecodeValue(new StringCustomAttributeProvider());
            var content = args.NamedArguments.Where(x => x.Name == "Content").First();
            return content.Value as string;
        }

        Dictionary<string, object> GetCustomAttrs(MrTypeAndMemberBase t)
        {
            var map = new Dictionary<string, object>();
            foreach (var a in t.GetCustomAttributes())
            {
                a.GetNameAndNamespace(out string name, out string ns);
                switch (name)
                {
                    case "DocDefaultAttribute":
                    case "DocStringAttribute":
                        {
                            var val = a.CustomAttribute;
                            map[name.Substring(0, name.IndexOf("Attribute")).ToLower()] = GetContentFromAttribute(val);
                            break;
                        }
                    case "ExperimentalAttribute":
                        {
                            Debug.WriteLine($"EXPERIMENTAL {t}");
                            map["experimental"] = true; break;
                        }
                    case "DeprecatedAttribute":
                        {
                            var val = a.CustomAttribute;
                            a.GetArguments(out var fixedArgs, out var namedArgs);
                            map["deprecated"] = fixedArgs[0].Value;
                            break;
                        }
                }
            }
            return map;
        }
        
        string GetTypeKind(MrType t)
        {
            if (t.IsClass && t.GetInvokeMethod() != null && t.GetBaseType().GetName() == "MulticastDelegate") return "delegate";
            if (t.IsClass) return "class";
            else if (t.IsInterface) return "interface";
            else if (t.IsEnum) return "enum";
            else if (t.IsStruct) return "struct";
            throw new ArgumentException();
        }

        XmlWriter writer;

        struct PropKey : IComparable<PropKey>
        {
            public string JSName { get; set; }
            public string FromXamlType { get; set; }

            public int CompareTo(PropKey other)
            {
                var r = JSName.CompareTo(other.JSName);
                if (r != 0) return r;
                return FromXamlType.CompareTo(other.FromXamlType);
            }
        }

        /*
   const std::map<std::string, std::function<xaml::DependencyObject()>> xamlTypeCreatorMap = {
    { "hyperlinkButton", CREATE_TYPE(HyperlinkButton)},
    { "textblock", CREATE_TYPE(TextBlock)},
  };

  const std::multimap<std::string, PropInfo> xamlPropertyMap = {
    { "width", { MAKE_GET_DP(FrameworkElement, WidthProperty), FromJSType::Double, XamlPropType::Double }},
    { "height", { MAKE_GET_DP(FrameworkElement, HeightProperty), FromJSType::Double, XamlPropType::Double }},
    { "text", { MAKE_GET_DP(ContentControl, ContentProperty), FromJSType::String, XamlPropType::Object }},
    { "text", { MAKE_GET_DP(TextBlock, TextProperty), FromJSType::String, XamlPropType::String }},
    { "color", { MAKE_GET_DP(Control, ForegroundProperty), FromJSType::SolidColorBrush, XamlPropType::Object }},
    { "color", { MAKE_GET_DP(TextBlock, ForegroundProperty), FromJSType::SolidColorBrush, XamlPropType::Object }},
    { "backgroundColor", { MAKE_GET_DP(Control, BackgroundProperty), FromJSType::SolidColorBrush, XamlPropType::Object }},
  };

    const std::map<std::string, std::function<void(IInspectable o, IReactContext context)> > xamlEventMap = {
        MAKE_EVENT(Click, xaml::Controls::Primitives::ButtonBase),
    };
         */

        void DumpTypes(string path)
        {
            //var output = Path.ChangeExtension(path, "xml");
            //writer = XmlWriter.Create(output, new XmlWriterSettings() { Indent = true });
            //writer.WriteStartDocument();
            var context = new MrLoadContext(true);
            context.FakeTypeRequired += (sender, e) => {
                var ctx = sender as MrLoadContext;
                if (e.AssemblyName == "Windows.Foundation.FoundationContract")
                {
                    e.ReplacementType = ctx.GetTypeFromAssembly(e.TypeName, "Windows");
                }
            };
            var windows_winmd = context.LoadAssemblyFromPath(@"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.19041.0\Windows.winmd");//, "Windows.winmd");
//            var assembly = context.LoadAssemblyFromPath(path); // @"C:\rnw\vnext\target\x86\Debug\Microsoft.ReactNative\Microsoft.ReactNative.winmd");
            context.FinishLoading();
            var types = windows_winmd.GetAllTypes().Skip(1);
            var fe = types.Where(type => IsFrameworkElementDerived(type));
            SortedDictionary<PropKey, string> output = new SortedDictionary<PropKey, string>();
            foreach (var type in fe)
            {
                foreach (var prop in type.GetProperties())
                {
                    // { "width", { MAKE_GET_DP(FrameworkElement, WidthProperty), FromJSType::Double, XamlPropType::Double } },
                    var name = prop.GetName();
                    if (prop.Getter.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Static)) continue;
                    var propType = prop.GetPropertyType();
                    var jsType = GetFromJSType(propType);
                    if (jsType != FromJSType.Null)
                    {
                        string jsname = name[0].ToString().ToLower() + name.Substring(1);
                        string rest = $"{{ MAKE_GET_DP({type.GetFullName().Replace(".", "::")}, {name}Property), FromJSType::{jsType.ToString()}, ... }}";
                        var key = new PropKey() { JSName = jsname, FromXamlType = type.GetFullName() };
                        output[key] = rest;
                    }
                    //var xamlPropType = GetXamlPropType(propType);
                }
            }
            foreach (var entry in output)
            {
                Console.WriteLine($"{{ \"{entry.Key.JSName}\", {entry.Value} }}");
            }

            Console.WriteLine($"{output.Count} properties");

            //var namespaces = assembly.GetAllTypes().Skip(1).Select(x => x.GetNamespace()).Distinct();
            //writer.WriteStartElement("assembly");
            //writer.WriteAttributeString("namespace", namespaces.FirstOrDefault());

            //foreach (var t in types)
            //{
            //    if (IsExclusiveInterface(t) || IsAttribute(t))
            //    {
            //        continue;
            //    }
            //    writer.WriteStartElement(GetTypeKind(t));
            //    writer.WriteAttributeString("name", t.GetName());
            //    if (!t.IsInterface && t.IsAbstract)
            //    {
            //        writer.WriteAttributeString("abstract", "true");
            //    }
            //    var attrs = GetCustomAttrs(t);
            //    WriteAttrs(attrs);

            //    Debug.WriteLine($"{GetTypeKind(t)} {t.GetName()} {attrs.GetValueOrDefault("docstring")} {attrs.GetValueOrDefault("docdefault")}");
            //    var kind = GetTypeKind(t);

            //    var baseTypesToSkip = new string[]
            //    {
            //        typeof(MulticastDelegate).FullName,
            //        typeof(ValueType).FullName,
            //        typeof(Enum).FullName,
            //        typeof(object).FullName,
            //        typeof(Attribute).FullName,
            //    };

            //    if (t.GetBaseType() != null && !baseTypesToSkip.Contains(t.GetBaseType().GetFullName()))
            //    {
            //        writer.WriteStartElement("extends");
            //        WriteType(t.GetBaseType());
            //        writer.WriteEndElement();
            //    }
            //    var ifaces = t.GetInterfaces().Where(x => !IsExclusiveInterface(x));
            //    if (ifaces.Count() > 0)
            //    {
            //        writer.WriteStartElement(t.IsClass ? "implements" : "extends");
            //        foreach (var i in ifaces)
            //        {
            //            WriteType(i);
            //        }
            //        writer.WriteEndElement();
            //    }


            //    foreach (var m in t.GetProperties())
            //    {
            //        var mattrs = GetCustomAttrs(m);
            //        var mattrsGetter = GetCustomAttrs(m.Getter);
            //        foreach (var kv in mattrsGetter) { mattrs[kv.Key] = kv.Value; }
            //        writer.WriteStartElement("property");
            //        writer.WriteAttributeString("name", m.GetName());
            //        bool isStatic = (m.Getter.MethodDefinition.Attributes & System.Reflection.MethodAttributes.Static) == System.Reflection.MethodAttributes.Static;
            //        if (isStatic) writer.WriteAttributeString("static", isStatic.ToString().ToLower());
            //        bool isReadonly = m.Setter == null || !m.Setter.GetParsedMethodAttributes().IsPublic;
            //        if (isReadonly) writer.WriteAttributeString("isReadonly", isReadonly.ToString().ToLower());
            //        WriteAttrs(mattrs);
            //        WriteType(m.GetPropertyType());

            //        writer.WriteEndElement();
            //        Debug.WriteLine($"  prop {m.GetPropertyType().GetPrettyFullName()} {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");

            //    }

            //    t.GetMethodsAndConstructors(out var methods, out var ctors);
            //    if (GetTypeKind(t) != "delegate")
            //    {
            //        foreach (var m in methods)
            //        {
            //            var mattrs = GetCustomAttrs(m);
            //            writer.WriteStartElement("method");
            //            writer.WriteAttributeString("name", m.GetName());
            //            bool isStatic = m.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Static);
            //            if (isStatic) writer.WriteAttributeString("static", isStatic.ToString().ToLower());
            //            bool isAbstract = !t.IsInterface && m.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Abstract);
            //            if (isAbstract) writer.WriteAttributeString("abstract", isAbstract.ToString().ToLower());

            //            WriteAttrs(mattrs);
            //            WriteMethodSignature(m);
            //            writer.WriteEndElement();
            //            Debug.WriteLine($"  method {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
            //        }

            //        foreach (var m in ctors)
            //        {
            //            var mattrs = GetCustomAttrs(m);
            //            writer.WriteStartElement("ctor");
            //            writer.WriteAttributeString("name", m.DeclaringType.GetName());

            //            WriteAttrs(mattrs);
            //            WriteMethodSignature(m, false);
            //            writer.WriteEndElement();
            //            Debug.WriteLine($"  ctor {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
            //        }
            //    }
            //    else
            //    {
            //        var m = t.GetInvokeMethod();
            //        var mattrs = GetCustomAttrs(m);

            //        WriteAttrs(mattrs);
            //        WriteMethodSignature(m);
            //    }

            //    foreach (var m in t.GetFields().Where(x => !x.IsSpecialName))
            //    {
            //        var mattrs = GetCustomAttrs(m);
            //        writer.WriteStartElement("field");
            //        writer.WriteAttributeString("name", m.GetName());
            //        var constant = m.GetConstantValue(out var type);
            //        if (constant != null)
            //            writer.WriteAttributeString("value", constant.ToString());
            //        WriteAttrs(mattrs);
            //        writer.WriteEndElement();
            //        Debug.WriteLine($"  field {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
            //    }

            //    foreach (var m in t.GetEvents())
            //    {
            //        var mattrs = GetCustomAttrs(m);
            //        writer.WriteStartElement("event");
            //        writer.WriteAttributeString("name", m.GetName());

            //        WriteAttrs(mattrs);
            //        writer.WriteEndElement();
            //        Debug.WriteLine($"  event {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
            //    }

            //    writer.WriteEndElement();
            //}
            //writer.WriteEndElement();
            //writer.WriteEndDocument();
            //writer.Flush();
            //writer.Close();
        }

        enum FromJSType
        {
            Null = 0,
            Object = 1,
            Array = 2,
            String = 3,
            Boolean = 4,
            Int64 = 5,
            Double = 6,
            SolidColorBrush = 7,
            Thickness = 8,
        }

        Dictionary<string, FromJSType> jsTypeMap = new Dictionary<string, FromJSType>()
        {
            { "System.String", FromJSType.String },
            { "System.Array", FromJSType.Array },
            { "System.Object", FromJSType.Object },
            { "System.Boolean", FromJSType.Boolean },
            { "System.Int32", FromJSType.Int64 },
            { "System.Int64", FromJSType.Int64 },
            { "System.Double", FromJSType.Double },
            { "System.Single", FromJSType.Double },
            { "Windows.UI.Xaml.Thickness", FromJSType.Thickness },
            { "Windows.UI.Xaml.HorizontalAlignment", FromJSType.String },
            { "Windows.UI.Xaml.VerticalAlignment", FromJSType.String },
            { "Windows.UI.Xaml.Media.Brush", FromJSType.SolidColorBrush },
            { "Windows.UI.Xaml.Media.SolidColorBrush", FromJSType.SolidColorBrush },
            { "Windows.UI.Text.FontWeight", FromJSType.String },
            { "Windows.UI.Text.FontStyle", FromJSType.String },
            { "Windows.UI.Text.FontStretch", FromJSType.String },
            { "Windows.UI.Xaml.Media.FontFamily", FromJSType.String },
            { "Windows.UI.Xaml.Input.KeyboardNavigationMode", FromJSType.String },
            { "Windows.UI.Xaml.Controls.ControlTemplate", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.FocusState", FromJSType.String },
            { "Windows.UI.Xaml.DependencyObject", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.RequiresPointer", FromJSType.String },
            { "Windows.UI.Xaml.ElementSoundMode", FromJSType.String },
            { "System.Uri", FromJSType.String },
            { "Windows.UI.Xaml.CornerRadius", FromJSType.Array },
            { "Windows.UI.Xaml.Controls.BackgroundSizing", FromJSType.String },
            { "Windows.UI.Xaml.Media.Animation.TransitionCollection", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.DataTemplateSelector", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.DataTemplate", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.UIElement", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.AppBarClosedDisplayMode", FromJSType.String },
            { "Windows.UI.Xaml.Controls.Primitives.AppBarTemplateSettings", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.LightDismissOverlayMode", FromJSType.String },
            { "System.Windows.Input.ICommand", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.ClickMode", FromJSType.String },
            { "Windows.UI.Xaml.Controls.Primitives.FlyoutBase", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.IconElement", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.CommandBarLabelPosition", FromJSType.String },
            { "Windows.UI.Xaml.Controls.Primitives.AppBarButtonTemplateSettings", FromJSType.Null }, // NYI
            { "System.Nullable`1", FromJSType.String }, // Nullable<bool> for IsChecked
            { "Windows.UI.Xaml.Controls.Primitives.AppBarToggleButtonTemplateSettings", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.ItemsPanelTemplate", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Controls.StyleSelector", FromJSType.Null }, // NYI
            { "Windows.UI.Xaml.Style", FromJSType.Null },
            { "Windows.UI.Xaml.Controls.GroupStyleSelector", FromJSType.Null },
            { "Windows.Foundation.Collections.IObservableVector`1", FromJSType.Null },
        };

        private FromJSType GetFromJSType(MrType propType)
        {
            if (propType.IsEnum) return FromJSType.String;
            if (jsTypeMap.ContainsKey(propType.GetFullName()))
            {
                return jsTypeMap[propType.GetFullName()];
            }
            else
            {
                Console.WriteLine(propType.GetPrettyFullName());
                return FromJSType.Null;
            }
        }

        private bool IsFrameworkElementDerived(MrType type)
        {
            if (type.GetBaseType() == null) return false;
            var bt = type.GetBaseType().GetFullName();
            if (bt == "Windows.UI.Xaml.FrameworkElement") return true;
            else return IsFrameworkElementDerived(type.GetBaseType());
        }

        private static bool IsAttribute(MrType t)
        {
            return (t.GetBaseType() != null && t.GetBaseType().GetFullName() == typeof(Attribute).FullName);
        }

        private static string MapManagedTypeToWinRtType(MrType t)
        {
            var map = new Dictionary<string, string> {
                { typeof(Object).FullName, "Windows.Foundation.IInspectable" },
                { "System.Collections.Generic.IList`1",  "Windows.Foundation.Collections.IVector`1" },
                { "System.Collections.Generic.IReadOnlyList`1", "Windows.Foundation.Collections.IVectorView`1" },
                { "System.Collections.Generic.IDictionary`2", "Windows.Foundation.Collections.IMap`2" },
                { "System.Collections.Generic.IReadOnlyDictionary`2", "Windows.Foundation.Collections.IMapView`2" },
            };

            if (map.ContainsKey(t.GetFullName()))
            {
                return map[t.GetFullName()];
            }
            if (t.GetNamespace() == "System")
            {
                return t.GetName();
            }
            else
            {
                return t.GetFullName();
            }
        }

        private void WriteType(MrType t)
        {
            var name = "";

            var gargs = t.GetGenericArguments();
            var gparams = t.GetGenericTypeParameters();
            name = GetSimpleTypeName(t, gargs, gparams);

            writer.WriteStartElement("type");
            if (t.IsArray)
            {
                writer.WriteAttributeString("array", "true");
                name = name.Replace("[]", "");
            }

            writer.WriteAttributeString("name", name);

            if (!gargs.IsEmpty)
            {
                writer.WriteAttributeString("generic", "true");
                writer.WriteStartElement("params");
                foreach (var p in gparams)
                {
                    WriteType(p);
                }
                writer.WriteEndElement();
            }
            else
            {
            }
            writer.WriteEndElement();
        }

        private static string GetSimpleTypeName(MrType t, ImmutableArray<MrType> gargs, ImmutableArray<MrType> gparams)
        {
            string name;
            if (t.IsTypeCode)
            {
                name = t.TypeCode.ToString();
            }
            else if (t.GetNamespace() == "System")
            {
                name = MapManagedTypeToWinRtType(t);
            }
            else if (!gargs.IsEmpty || !gparams.IsEmpty)
            {
                name = MapManagedTypeToWinRtType(t);
                name = name.Substring(0, name.IndexOf('`'));

            }
            else
            {
                name = t.GetFullName();
            }

            return name;
        }

        private static bool IsExclusiveInterface(MrType t)
        {
            return t.IsInterface && t.GetCustomAttributes().Any(IsExclusiveToAttribute);
        }

        private static bool IsExclusiveToAttribute(MrCustomAttribute ca)
        {
            ca.GetNameAndNamespace(out var name, out var ns);
            return name == "ExclusiveToAttribute";
        }

        private void WriteMethodSignature(MrMethod m, bool returnType = true)
        {
            if (returnType) WriteType(m.MethodSignature.ReturnType);

            writer.WriteStartElement("params");
            foreach (var p in m.GetParameters())
            {
                writer.WriteStartElement("param");
                writer.WriteAttributeString("name", p.GetParameterName());
                WriteType(p.GetParameterType());

                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private void WriteAttrs(Dictionary<string, object> attrs)
        {
            if (attrs.ContainsKey("experimental")) writer.WriteAttributeString("experimental", attrs["experimental"].ToString().ToLower());

            if (attrs.ContainsKey("docstring")) writer.WriteElementString("summary", attrs["docstring"] as string);
            if (attrs.ContainsKey("docdefault")) writer.WriteElementString("default", attrs["docdefault"] as string);
            if (attrs.ContainsKey("deprecated")) writer.WriteElementString("deprecated", attrs["deprecated"] as string);
        }

        static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: DumpWinMd <assembly.winmd>");
                Console.WriteLine("Dumps information from a Windows Metadata file onto XML");
            }
            else
            {
                new Program().DumpTypes(args[0]);
            }
        }
        
    }
}
