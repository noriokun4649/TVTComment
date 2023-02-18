using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.Schema;
using System.Xml;

namespace TVTComment.Model.NiconicoUtils
{
    static class NiconicoForceValidation
    {
        public static async Task CheckAsync(string uri)
        {
            var xsdMarkup = @"<?xml version=""1.0"" encoding=""UTF-8""?>
                                <xs:schema attributeFormDefault=""unqualified"" elementFormDefault=""qualified"" xmlns:xs=""http://www.w3.org/2001/XMLSchema"">
                                  <xs:element name=""channels"" type=""channelsType""/>
                                  <xs:complexType name=""bs_channelType"">
                                    <xs:sequence>
                                      <xs:element type=""xs:int"" name=""id""/>
                                      <xs:element type=""xs:string"" name=""name""/>
                                      <xs:element type=""xs:string"" name=""video""/>
                                      <xs:element type=""threadType"" name=""thread""/>
                                    </xs:sequence>
                                  </xs:complexType>
                                  <xs:complexType name=""threadType"">
                                    <xs:sequence>
                                      <xs:element type=""xs:int"" name=""id""/>
                                      <xs:element type=""xs:string"" name=""last_res""/>
                                      <xs:element type=""xs:int"" name=""force""/>
                                      <xs:element type=""xs:int"" name=""viewers""/>
                                      <xs:element type=""xs:int"" name=""comments""/>
                                    </xs:sequence>
                                  </xs:complexType>
                                  <xs:complexType name=""channelType"">
                                    <xs:sequence>
                                      <xs:element type=""xs:int"" name=""id""/>
                                      <xs:element type=""xs:int"" name=""no""/>
                                      <xs:element type=""xs:string"" name=""name""/>
                                      <xs:element type=""xs:string"" name=""video""/>
                                      <xs:element type=""threadType"" name=""thread""/>
                                    </xs:sequence>
                                  </xs:complexType>
                                  <xs:complexType name=""channelsType"">
                                    <xs:sequence>
                                      <xs:element type=""channelType"" name=""channel"" maxOccurs=""unbounded"" minOccurs=""0""/>
                                      <xs:element type=""bs_channelType"" name=""bs_channel"" maxOccurs=""unbounded"" minOccurs=""0""/>
                                    </xs:sequence>
                                    <xs:attribute type=""xs:string"" name=""status""/>
                                  </xs:complexType>
                                </xs:schema>";
            XmlSchemaSet schemas = new XmlSchemaSet();
            schemas.Add("", XmlReader.Create(new StringReader(xsdMarkup)));
            var errors = false;
            var message = "";

            var httpClient = new HttpClient();
            var stream = await httpClient.GetStreamAsync(uri);
            var doc = XDocument.Load(stream);
            var xrs = new XmlReaderSettings();
            xrs.ValidationType = ValidationType.Schema;
            xrs.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
            xrs.Schemas = schemas;
            xrs.ValidationEventHandler += (o, s) => {
                message = s.Message;
                errors = true;
            };

            using (XmlReader xr = XmlReader.Create(doc.CreateReader(), xrs))
            {
                while (xr.Read()) { }

            }

            if (errors)
            {
                throw new ValidationException(message);
            }

        }
    }
}
