/* ====================================================================
   Licensed to the Apache Software Foundation (ASF) under one or more

   contributor license agreements.  See the NOTICE file distributed with
   this work for Additional information regarding copyright ownership.
   The ASF licenses this file to You under the Apache License, Version 2.0
   (the "License"); you may not use this file except in compliance with
   the License.  You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
==================================================================== */

namespace NPOI.XWPF.UserModel
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;

    using NPOI.Util;
    using NPOI.OpenXml4Net.OPC;
    using NPOI.OpenXmlFormats.Wordprocessing;
    using NPOI.WP.UserModel;
    using NPOI.XWPF.Model;
    using NPOI.OOXML.XWPF.Util;
    using NPOI.POIFS.Crypt;

    /**
     * <p>High(ish) level class for working with .docx files.</p>
     * 
     * <p>This class tries to hide some of the complexity
     *  of the underlying file format, but as it's not a 
     *  mature and stable API yet, certain parts of the
     *  XML structure come through. You'll therefore almost
     *  certainly need to refer to the OOXML specifications
     *  from
     *  http://www.ecma-international.org/publications/standards/Ecma-376.htm
     *  at some point in your use.</p>
     */
    public class XWPFDocument : POIXMLDocument, Document, IBody, IDisposable
    {
        private CT_Document ctDocument;
        private XWPFSettings settings;

        /**
         * Keeps track on all id-values used in this document and included parts, like headers, footers, etc.
         */
        private readonly IdentifierManager drawingIdManager = new IdentifierManager(0L, 4294967295L);
        protected List<XWPFFooter> footers = new List<XWPFFooter>();
        protected List<XWPFHeader> headers = new List<XWPFHeader>();
        protected List<XWPFHyperlink> hyperlinks = new List<XWPFHyperlink>();
        protected List<XWPFParagraph> paragraphs = new List<XWPFParagraph>();
        protected List<XWPFTable> tables = new List<XWPFTable>();
        protected List<XWPFSDT> contentControls = new List<XWPFSDT>();
        protected List<IBodyElement> bodyElements = new List<IBodyElement>();
        protected List<XWPFPictureData> pictures = new List<XWPFPictureData>();
        protected Dictionary<long, List<XWPFPictureData>> packagePictures = new Dictionary<long, List<XWPFPictureData>>();
        protected Dictionary<int, XWPFFootnote> endnotes = new Dictionary<int, XWPFFootnote>();
        protected XWPFNumbering numbering;
        protected XWPFStyles styles;
        protected XWPFFootnotes footnotes;
        private XWPFComments comments;

        /** Handles the joy of different headers/footers for different pages */
        private XWPFHeaderFooterPolicy headerFooterPolicy;

        public XWPFDocument(OPCPackage pkg)
            : base(pkg)
        {
            //build a tree of POIXMLDocumentParts, this document being the root
            Load(XWPFFactory.GetInstance());
        }

        public XWPFDocument(Stream is1)
            : base(PackageHelper.Open(is1))
        {

            //build a tree of POIXMLDocumentParts, this workbook being the root
            Load(XWPFFactory.GetInstance());
        }

        public XWPFDocument()
            : base(NewPackage())
        {
            OnDocumentCreate();
        }


        internal override void OnDocumentRead()
        {
            try {
                XmlDocument xmldoc = DocumentHelper.LoadDocument(GetPackagePart().GetInputStream());
                DocumentDocument doc = DocumentDocument.Parse(xmldoc, NamespaceManager);
                ctDocument = doc.Document;

                InitFootnotes();
                // parse the document with cursor and add
                //    // the XmlObject to its lists
                List<CT_Body> allBody = [ctDocument.body];
                if(ctDocument.bodyList != null)
                    allBody.AddRange(ctDocument.bodyList);
                foreach(CT_Body body in allBody)
                {
                    foreach (object o in body.Items)
                    {
                        if (o is CT_P ctP)
                        {
                            XWPFParagraph p = new XWPFParagraph(ctP, this);
                            bodyElements.Add(p);
                            paragraphs.Add(p);
                        }
                        else if (o is CT_Tbl tbl)
                        {
                            XWPFTable t = new XWPFTable(tbl, this);
                            bodyElements.Add(t);
                            tables.Add(t);
                        }
                        else if (o is CT_SdtBlock block)
                        {
                            XWPFSDT c = new XWPFSDT(block, this);
                            bodyElements.Add(c);
                            contentControls.Add(c);
                        }
                    }
                }
                // Sort out headers and footers
                if (doc.Document.body?.sectPr != null)
                    headerFooterPolicy = new XWPFHeaderFooterPolicy(this);

                // Create for each XML-part in the Package a PartClass
                foreach (RelationPart rp in RelationParts) {
                    POIXMLDocumentPart p = rp.DocumentPart;
                    String relation = rp.Relationship.RelationshipType;
                    if (relation.Equals(XWPFRelation.STYLES.Relation))
                    {
                        this.styles = (XWPFStyles)p;
                        this.styles.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.NUMBERING.Relation))
                    {
                        this.numbering = (XWPFNumbering)p;
                        this.numbering.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.FOOTER.Relation))
                    {
                        XWPFFooter footer = (XWPFFooter)p;
                        footers.Add(footer);
                        footer.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.HEADER.Relation))
                    {
                        XWPFHeader header = (XWPFHeader)p;
                        headers.Add(header);
                        header.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.COMMENT.Relation))
                    {
                        this.comments = (XWPFComments)p;
                        this.comments.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.SETTINGS.Relation))
                    {
                        settings = (XWPFSettings)p;
                        settings.OnDocumentRead();
                    }
                    else if (relation.Equals(XWPFRelation.IMAGES.Relation))
                    {
                        XWPFPictureData picData = (XWPFPictureData)p;
                        picData.OnDocumentRead();
                        RegisterPackagePictureData(picData);
                        pictures.Add(picData);
                    }
                    else if (relation.Equals(XWPFRelation.GLOSSARY_DOCUMENT.Relation))
                    {
                        // We don't currently process the glossary itself
                        // Until we do, we do need to load the glossary child parts of it
                        foreach (POIXMLDocumentPart gp in p.GetRelations())
                        {
                            // Trigger the onDocumentRead for all the child parts
                            // Otherwise we'll hit issues on Styles, Settings etc on save
                            try
                            {
                                gp.OnDocumentRead();
                                //Method onDocumentRead = gp.getClass().getDeclaredMethod("onDocumentRead");
                                //onDocumentRead.setAccessible(true);
                                //onDocumentRead.invoke(gp);
                            }
                            catch (Exception e)
                            {
                                throw new POIXMLException(e);
                            }
                        }
                    }
                }
                InitHyperlinks();
            }
            catch (XmlException e)
            {
                throw new POIXMLException(e);
            }
            
        }

        private void InitHyperlinks()
        {
            // Get the hyperlinks
            // TODO: make me optional/Separated in private function
            try
            {
                IEnumerator<PackageRelationship> relIter =
                    GetPackagePart().GetRelationshipsByType(XWPFRelation.HYPERLINK.Relation).GetEnumerator();
                while (relIter.MoveNext())
                {
                    PackageRelationship rel = relIter.Current;
                    hyperlinks.Add(new XWPFHyperlink(rel.Id, rel.TargetUri.OriginalString));
                }
            }
            catch (InvalidDataException e)
            {
                throw new POIXMLException(e);
            }
            hyperlinks.AddRange(footers.SelectMany(footer => footer.GetHyperlinks()));
            if (footnotes != null)
            {
                hyperlinks.AddRange(footnotes.GetHyperlinks());
            }
        }

        private void InitFootnotes()
        {
            foreach(RelationPart rp in RelationParts){
                POIXMLDocumentPart p = rp.DocumentPart;
                String relation = rp.Relationship.RelationshipType;
                if (relation.Equals(XWPFRelation.FOOTNOTE.Relation)) {
                  this.footnotes = (XWPFFootnotes)p;
                  this.footnotes.OnDocumentRead();
               }
               if (relation.Equals(XWPFRelation.ENDNOTE.Relation))
               {
                   XmlDocument xmldoc = ConvertStreamToXml(p.GetPackagePart().GetInputStream());
                   EndnotesDocument endnotesDocument = EndnotesDocument.Parse(xmldoc, NamespaceManager);
                   foreach (CT_FtnEdn ctFtnEdn in endnotesDocument.Endnotes.endnote)
                   {
                       endnotes.Add(ctFtnEdn.id, new XWPFFootnote(this, ctFtnEdn));
                   }
               }
            }
        }

        /**
         * Create a new WordProcessingML package and Setup the default minimal content
         */
        protected static OPCPackage NewPackage()
        {
             try {
                OPCPackage pkg = OPCPackage.Create(new MemoryStream());
                // Main part
                PackagePartName corePartName = PackagingUriHelper.CreatePartName(XWPFRelation.DOCUMENT.DefaultFileName);
                // Create main part relationship
                pkg.AddRelationship(corePartName, TargetMode.Internal, PackageRelationshipTypes.CORE_DOCUMENT);
                // Create main document part
                pkg.CreatePart(corePartName, XWPFRelation.DOCUMENT.ContentType);

                pkg.GetPackageProperties().SetCreatorProperty(DOCUMENT_CREATOR);
                return pkg;
             }
             catch (Exception e)
             {
                 throw new POIXMLException(e);
             }
        }

        /**
         * Create a new CT_Document with all values Set to default
         */

        internal override void OnDocumentCreate()
        {
            ctDocument = new CT_Document();
            ctDocument.AddNewBody();
            

            settings = (XWPFSettings) CreateRelationship(XWPFRelation.SETTINGS,XWPFFactory.GetInstance());
            CreateStyles();

            ExtendedProperties expProps = GetProperties().ExtendedProperties;
            expProps.GetUnderlyingProperties().Application = (DOCUMENT_CREATOR);
        }

        /**
         * Returns the low level document base object
         */
        //CTDocument1
        public CT_Document Document
        {
            get
            {
                return ctDocument;
            }
            set
            {
                ctDocument = value;
            }
        }
        /**
         * Sets columns on document base object
         */
        public int ColumnCount
        {
            get
            {
                return int.Parse(ctDocument.body.sectPr.cols.num);
            }
            set
            {
                if (ctDocument != null)
                {
                    ctDocument.body.sectPr.cols.num = value.ToString();
                }

            }
            
        }
        /**
         * Sets Text Direction of Document
         */
         public ST_TextDirection TextDirection
        {
            get
            {
                return ctDocument.body.sectPr.textDirection.val;
            }
            set
            {
                if (ctDocument != null)
                {
                    ctDocument.body.sectPr.textDirection.val = value;
                }
            }
            
        }
        internal IdentifierManager DrawingIdManager
        {
            get
            {
                return drawingIdManager;
            }
        }

        /**
         * returns an Iterator with paragraphs and tables
         * @see NPOI.XWPF.UserModel.IBody#getBodyElements()
         */
        public IList<IBodyElement> BodyElements
        {
            get
            {
                return bodyElements.AsReadOnly();
            }
        }
        public IEnumerator<IBodyElement> GetBodyElementsIterator()
        {
            return bodyElements.GetEnumerator();
        }
        /**
         * @see NPOI.XWPF.UserModel.IBody#getParagraphs()
         */
        public IList<XWPFParagraph> Paragraphs
        {
            get
            {
                return paragraphs.AsReadOnly();
            }
        }

        /**
         * @see NPOI.XWPF.UserModel.IBody#getTables()
         */
        public IList<XWPFTable> Tables
        {
            get
            {
                return tables.AsReadOnly();
            }
        }

        /**
         * @see NPOI.XWPF.UserModel.IBody#getTableArray(int)
         */
        public XWPFTable GetTableArray(int pos)
        {
            if (pos >= 0 && pos < tables.Count)
            {
                return tables[(pos)];
            }
            return null;
        }

        /**
         * 
         * @return  the list of footers
         */
        public IList<XWPFFooter> FooterList
        {
            get
            {
                return footers.AsReadOnly();
            }
        }

        public XWPFFooter GetFooterArray(int pos)
        {
            if (pos >= 0 && pos < footers.Count)
            {
                return footers[(pos)];
            }
            return null;
        }

        /**
         * 
         * @return  the list of headers
         */
        public IList<XWPFHeader> HeaderList
        {
            get
            {
                return headers.AsReadOnly();
            }
        }

        public XWPFHeader GetHeaderArray(int pos)
        {
            if (pos >= 0 && pos < headers.Count)
            {
                return headers[(pos)];
            }
            return null;
            
        }

        public String GetTblStyle(XWPFTable table)
        {
            return table.StyleID;
        }

        public XWPFHyperlink GetHyperlinkByID(String id)
        {
            foreach (XWPFHyperlink link in hyperlinks)
            {
                if (link.Id.Equals(id))
                    return link;
            }

            return null;
        }

        public XWPFFootnote GetFootnoteByID(int id)
        {
            return footnotes?.GetFootnoteById(id);
        }

        public Dictionary<int, XWPFFootnote> Endnotes => endnotes;

        public XWPFFootnote GetEndnoteByID(int id)
        {
            if (endnotes == null || !endnotes.TryGetValue(id, out XWPFFootnote byId)) 
                return null;
            return byId;
        }

        public List<XWPFFootnote> GetFootnotes()
        {
            if (footnotes == null)
            {
                return [];
            }
            return footnotes.GetFootnotesList();
        }

        public XWPFHyperlink[] GetHyperlinks()
        {
            return hyperlinks.ToArray();
        }

        /**
         * Get Comments
         *
         * @return comments
         */
        public XWPFComments GetDocComments()
        {
            return comments;
        }

        public XWPFComment GetCommentByID(String id)
        {
            if (null == comments)
            {
                return null;
            }
            return comments.GetCommentByID(id);
        }

        public XWPFComment[] GetComments()
        {
            if (null == comments)
            {
                return null;
            }
            return comments.GetComments().ToArray();
        }

        /**
         * Get the document part that's defined as the
         *  given relationship of the core document.
         */
        public PackagePart GetPartById(String id)
        {
            try
            {
                PackagePart corePart = CorePart;
                return corePart.GetRelatedPart(corePart.GetRelationship(id));
            }
            catch (Exception e)
            {
                throw new ArgumentException("GetTargetPart exception", e);
            }
        }

        /**
         * Returns the policy on headers and footers, which
         *  also provides a way to Get at them.
         */
        public XWPFHeaderFooterPolicy GetHeaderFooterPolicy()
        {
            return headerFooterPolicy;
        }
        public XWPFHeaderFooterPolicy CreateHeaderFooterPolicy()
        {
            if (headerFooterPolicy == null)
            {
                //if (!ctDocument.body.IsSetSectPr())
                //{
                //    ctDocument.body.AddNewSectPr();
                //}
                headerFooterPolicy = new XWPFHeaderFooterPolicy(this);
            }
            return headerFooterPolicy;
        }

        /**
         * Create a header of the given type
         *
         * @param type {@link HeaderFooterType} enum
         * @return object of type {@link XWPFHeader}
         */
        public XWPFHeader CreateHeader(HeaderFooterType type)
        {
            XWPFHeaderFooterPolicy hfPolicy = CreateHeaderFooterPolicy();
            // TODO this needs to be migrated out into section code
            if (type == HeaderFooterType.FIRST)
            {
                CT_SectPr ctSectPr = GetSection();
                if (ctSectPr.IsSetTitlePg() == false)
                {
                    CT_OnOff titlePg = ctSectPr.AddNewTitlePg();
                    titlePg.val = true;//ST_OnOff.on;
                }
            }
            else if (type == HeaderFooterType.EVEN)
            {
                // TODO Add support for Even/Odd headings and footers
            }
            return hfPolicy.CreateHeader(EnumConverter.ValueOf<ST_HdrFtr, HeaderFooterType>(type));
        }

        /**
         * Create a footer of the given type
         *
         * @param type {@link HeaderFooterType} enum
         * @return object of type {@link XWPFFooter}
         */
        public XWPFFooter CreateFooter(HeaderFooterType type)
        {
            XWPFHeaderFooterPolicy hfPolicy = CreateHeaderFooterPolicy();
            // TODO this needs to be migrated out into section code
            if (type == HeaderFooterType.FIRST)
            {
                CT_SectPr ctSectPr = GetSection();
                if (ctSectPr.IsSetTitlePg() == false)
                {
                    CT_OnOff titlePg = ctSectPr.AddNewTitlePg();
                    titlePg.val = true;//ST_OnOff.on;
                }
            }
            else if (type == HeaderFooterType.EVEN)
            {
                // TODO Add support for Even/Odd headings and footers
            }
            return hfPolicy.CreateFooter(EnumConverter.ValueOf<ST_HdrFtr, HeaderFooterType>(type));
        }

        /**
         * Return the {@link CTSectPr} object that corresponds with the
         * last section in this document.
         *
         * @return {@link CTSectPr} object
         */
        private CT_SectPr GetSection()
        {
            CT_Body ctBody = Document.body;
            return (ctBody.IsSetSectPr() ?
                    ctBody.sectPr :
                    ctBody.AddNewSectPr());
        }

        /**
         * Returns the styles object used
         */

        public CT_Styles GetCTStyle()
        {
            PackagePart[] parts;
            try {
                parts = GetRelatedByType(XWPFRelation.STYLES.Relation);
            } catch(Exception e) {
                throw new InvalidOperationException("get Style document part exception", e);
            }
            if(parts.Length != 1) {
                throw new InvalidOperationException("Expecting one Styles document part, but found " + parts.Length);
            }
            XmlDocument xmldoc = ConvertStreamToXml(parts[0].GetInputStream());
            StylesDocument sd = StylesDocument.Parse(xmldoc, NamespaceManager);
            return sd.Styles;
        }

        /**
         * Get the document's embedded files.
         */

        public override List<PackagePart> GetAllEmbedds()
        {
            List<PackagePart> embedds = new List<PackagePart>();
            PackagePart part = GetPackagePart();
            // Get the embeddings for the workbook
            foreach (PackageRelationship rel in GetPackagePart().GetRelationshipsByType(OLE_OBJECT_REL_TYPE))
            {
                embedds.Add(part.GetRelatedPart(rel));
            }

            foreach (PackageRelationship rel in GetPackagePart().GetRelationshipsByType(PACK_OBJECT_REL_TYPE))
            {
                embedds.Add(part.GetRelatedPart(rel));
            }

            return embedds;
            
        }

        /**
         * Finds that for example the 2nd entry in the body list is the 1st paragraph
         */
        private int GetBodyElementSpecificPos(int pos, List<IBodyElement> list)
        {
            // If there's nothing to Find, skip it
            if (list.Count == 0)
            {
                return -1;
            }

            if(pos >= 0 && pos < bodyElements.Count) {
               // Ensure the type is correct
               IBodyElement needle = bodyElements[(pos)];
               if (needle.ElementType != list[(0)].ElementType)
               {
                   // Wrong type
                   return -1;
               }

               // Work back until we find it
               int startPos = Math.Min(pos, list.Count - 1);
               for (int i = startPos; i >= 0; i--)
               {
                   if (list[(i)] == needle)
                   {
                       return i;
                   }
               }
            }

            // Couldn't be found
            return -1;
            throw new NotImplementedException();
        }

        /**
         * Look up the paragraph at the specified position in the body elemnts list
         * and return this paragraphs position in the paragraphs list
         * 
         * @param pos
         *            The position of the relevant paragraph in the body elements
         *            list
         * @return the position of the paragraph in the paragraphs list, if there is
         *         a paragraph at the position in the bodyelements list. Else it
         *         will return -1
         * 
         */
        public int GetParagraphPos(int pos)
        {
            List<IBodyElement> list = new List<IBodyElement>();
            foreach (IBodyElement p in paragraphs)
            {
                list.Add(p);
            }
            return GetBodyElementSpecificPos(pos, list);
        }

        /**
         * Get with the position of a table in the bodyelement array list 
         * the position of this table in the table array list
         * @param pos position of the table in the bodyelement array list
         * @return if there is a table at the position in the bodyelement array list,
         * 		   else it will return null. 
         */
        public int GetTablePos(int pos)
        {
            List<IBodyElement> list = new List<IBodyElement>();
            foreach (IBodyElement p in tables)
            {
                list.Add(p);
            }
            return GetBodyElementSpecificPos(pos, list);
        }

        /**
         * Add a new paragraph at position of the cursor. The cursor must be on the
         * {@link org.apache.xmlbeans.XmlCursor.TokenType#START} tag of an subelement
         * of the documents body. When this method is done, the cursor passed as
         * parameter points to the {@link org.apache.xmlbeans.XmlCursor.TokenType#END}
         * of the newly inserted paragraph.
         * 
         * @param cursor
         * @return the {@link XWPFParagraph} object representing the newly inserted
         *         CTP object
         */
        public XWPFParagraph InsertNewParagraph(/*XmlCursor*/XmlDocument cursor)
        {
            //if (isCursorInBody(cursor)) {
            //    String uri = CTP.type.Name.NamespaceURI;
            //    /*
            //     * TODO DO not use a coded constant, find the constant in the OOXML
            //     * classes instead, as the child of type CT_Paragraph is defined in the 
            //     * OOXML schema as 'p'
            //     */
            //    String localPart = "p";
            //    // Creates a new Paragraph, cursor is positioned inside the new
            //    // element
            //    cursor.BeginElement(localPart, uri);
            //    // Move the cursor to the START token to the paragraph just Created
            //    cursor.ToParent();
            //    CTP p = (CTP) cursor.Object;
            //    XWPFParagraph newP = new XWPFParagraph(p, this);
            //    XmlObject o = null;
            //    /*
            //     * Move the cursor to the previous element until a) the next
            //     * paragraph is found or b) all elements have been passed
            //     */
            //    while (!(o is CTP) && (cursor.ToPrevSibling())) {
            //        o = cursor.Object;
            //    }
            //    /*
            //     * if the object that has been found is a) not a paragraph or b) is
            //     * the paragraph that has just been inserted, as the cursor in the
            //     * while loop above was not Moved as there were no other siblings,
            //     * then the paragraph that was just inserted is the first paragraph
            //     * in the body. Otherwise, take the previous paragraph and calculate
            //     * the new index for the new paragraph.
            //     */
            //    if ((!(o is CTP)) || (CTP) o == p) {
            //        paragraphs.Add(0, newP);
            //    } else {
            //        int pos = paragraphs.IndexOf(getParagraph((CTP) o)) + 1;
            //        paragraphs.Add(pos, newP);
            //    }

            //    /*
            //     * create a new cursor, that points to the START token of the just
            //     * inserted paragraph
            //     */
            //    XmlCursor newParaPos = p.NewCursor();
            //    try {
            //        /*
            //         * Calculate the paragraphs index in the list of all body
            //         * elements
            //         */
            //        int i = 0;
            //        cursor.ToCursor(newParaPos);
            //        while (cursor.ToPrevSibling()) {
            //            o = cursor.Object;
            //            if (o is CTP || o is CTTbl)
            //                i++;
            //        }
            //        bodyElements.Add(i, newP);
            //        cursor.ToCursor(newParaPos);
            //        cursor.ToEndToken();
            //        return newP;
            //    } finally {
            //        newParaPos.Dispose();
            //    }
            //}
            //return null;
            throw new NotImplementedException();
        }

        public XWPFTable InsertNewTbl(/*XmlCursor*/XmlDocument cursor)
        {
            //    if (isCursorInBody(cursor)) {
            //    String uri = CTTbl.type.getName().getNamespaceURI();
            //    String localPart = "tbl";
            //    cursor.beginElement(localPart, uri);
            //    cursor.toParent();
            //    CTTbl t = (CTTbl) cursor.getObject();
            //    XWPFTable newT = new XWPFTable(t, this);
            //    XmlObject o = null;
            //    while (!(o instanceof CTTbl) && (cursor.toPrevSibling())) {
            //        o = cursor.getObject();
            //    }
            //    if (!(o instanceof CTTbl)) {
            //        tables.add(0, newT);
            //    } else {
            //        int pos = tables.indexOf(getTable((CTTbl) o)) + 1;
            //        tables.add(pos, newT);
            //    }
            //    int i = 0;
            //    XmlCursor tableCursor = t.newCursor();
            //    try {
            //        cursor.toCursor(tableCursor);
            //    while (cursor.toPrevSibling()) {
            //        o = cursor.getObject();
            //        if (o instanceof CTP || o instanceof CTTbl)
            //            i++;
            //    }
            //    bodyElements.add(i, newT);
            //        cursor.toCursor(tableCursor);
            //    cursor.toEndToken();
            //    return newT;
            //}
            //    finally {
            //        tableCursor.dispose();
            //    }
            //}
            //return null;
            throw new NotImplementedException();
        }

        /**
         * verifies that cursor is on the right position
         * @param cursor
         */
        private bool IsCursorInBody(/*XmlCursor*/XmlDocument cursor)
        {
            /*XmlCursor verify = cursor.NewCursor();
            verify.ToParent();
            try {
                return (verify.Object == this.ctDocument.Body);
            } finally {
                verify.Dispose();
            }*/
            throw new NotImplementedException();
        }

        private int GetPosOfBodyElement(IBodyElement needle)
        {
            BodyElementType type = needle.ElementType;
            IBodyElement current;
            for (int i = 0; i < bodyElements.Count; i++)
            {
                current = bodyElements[(i)];
                if (current.ElementType == type)
                {
                    if (current.Equals(needle))
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        /**
         * Get the position of the paragraph, within the list
         *  of all the body elements.
         * @param p The paragraph to find
         * @return The location, or -1 if the paragraph couldn't be found 
         */
        public int GetPosOfParagraph(XWPFParagraph p)
        {
            return GetPosOfBodyElement(p);
        }

        /**
         * Get the position of the table, within the list of
         *  all the body elements.
         * @param t The table to find
         * @return The location, or -1 if the table couldn't be found
         */
        public int GetPosOfTable(XWPFTable t)
        {
            return GetPosOfBodyElement(t);
        }

        /**
         * Commit and saves the document
         */

        protected internal override void Commit()
        {

            //XmlOptions xmlOptions = new XmlOptions(DEFAULT_XML_OPTIONS);
            //xmlOptions.SaveSyntheticDocumentElement=(new QName(CTDocument1.type.Name.NamespaceURI, "document"));
            //Dictionary<String, String> map = new Dictionary<String, String>();
            //map.Put("http://schemas.Openxmlformats.org/officeDocument/2006/math", "m");
            //map.Put("urn:schemas-microsoft-com:office:office", "o");
            //map.Put("http://schemas.Openxmlformats.org/officeDocument/2006/relationships", "r");
            //map.Put("urn:schemas-microsoft-com:vml", "v");
            //map.Put("http://schemas.Openxmlformats.org/markup-compatibility/2006", "ve");
            //map.Put("http://schemas.Openxmlformats.org/wordProcessingml/2006/main", "w");
            //map.Put("urn:schemas-microsoft-com:office:word", "w10");
            //map.Put("http://schemas.microsoft.com/office/word/2006/wordml", "wne");
            //map.Put("http://schemas.Openxmlformats.org/drawingml/2006/wordProcessingDrawing", "wp");
            //xmlOptions.SaveSuggestedPrefixes=(map);

            PackagePart part = GetPackagePart();
            using (Stream out1 = part.GetOutputStream())
            {
                DocumentDocument doc = new DocumentDocument(ctDocument);
                doc.Save(out1);
            }
        }

        /**
         * Gets the index of the relation we're trying to create
         * @param relation
         * @return i
         */
        private int GetRelationIndex(XWPFRelation relation)
        {
            int i = 1;
            foreach (RelationPart rp in RelationParts)
            {
                if (rp.Relationship.RelationshipType.Equals(relation.Relation))
                {
                    i++;
                }
            }
            return i;
        }

        /**
         * Appends a new paragraph to this document
         * @return a new paragraph
         */
        public XWPFParagraph CreateParagraph()
        {
            XWPFParagraph p = new XWPFParagraph(ctDocument.body.AddNewP(), this);
            bodyElements.Add(p);
            paragraphs.Add(p);
            return p;
        }

        /**
         * Creates an empty comments for the document if one does not already exist
         *
         * @return comments
         */
        public XWPFComments CreateComments()
        {
            if (comments == null)
            {
                CommentsDocument commentsDoc = new CommentsDocument();

                XWPFRelation relation = XWPFRelation.COMMENT;
                int i = GetRelationIndex(relation);

                XWPFComments wrapper = (XWPFComments)CreateRelationship(relation, XWPFFactory.GetInstance(), i);
                wrapper.SetCtComments(commentsDoc.AddNewComments());
                wrapper.SetXWPFDocument(GetXWPFDocument());
                comments = wrapper;
            }
            return comments;
        }

        /**
         * Creates an empty numbering if one does not already exist and Sets the numbering member
         * @return numbering
         */
        public XWPFNumbering CreateNumbering()
        {
            if(numbering == null) {
                NumberingDocument numberingDoc = new NumberingDocument();

                XWPFRelation relation = XWPFRelation.NUMBERING;
                int i = GetRelationIndex(relation);

                XWPFNumbering wrapper = (XWPFNumbering)CreateRelationship(relation, XWPFFactory.GetInstance(), i);
                wrapper.SetNumbering(numberingDoc.Numbering);
                numbering = wrapper;
            }

            return numbering;
        }

        /**
         * Creates an empty styles for the document if one does not already exist
         * @return styles
         */
        public XWPFStyles CreateStyles()
        {
            if (styles == null)
            {
                StylesDocument stylesDoc = new StylesDocument();

                XWPFRelation relation = XWPFRelation.STYLES;
                int i = GetRelationIndex(relation);

                XWPFStyles wrapper = (XWPFStyles)CreateRelationship(relation, XWPFFactory.GetInstance(), i);
                wrapper.SetStyles(stylesDoc.Styles);
                styles = wrapper;
            }

            return styles;
        }

        /**
         * Creates an empty footnotes element for the document if one does not already exist
         * @return footnotes
         */
        public XWPFFootnotes CreateFootnotes()
        {
            if (footnotes == null)
            {
                FootnotesDocument footnotesDoc = new FootnotesDocument();

                XWPFRelation relation = XWPFRelation.FOOTNOTE;
                int i = GetRelationIndex(relation);

                XWPFFootnotes wrapper = (XWPFFootnotes)CreateRelationship(relation, XWPFFactory.GetInstance(), i);
                wrapper.SetFootnotes(footnotesDoc.Footnotes);
                footnotes = wrapper;
            }

            return footnotes;
        }

        public XWPFFootnote AddFootnote(CT_FtnEdn note)
        {
            return footnotes.AddFootnote(note);
        }

        public XWPFFootnote AddEndnote(CT_FtnEdn note)
        {
            XWPFFootnote endnote = new XWPFFootnote(this, note);
            endnotes.Add(note.id, endnote);
            return endnote;
        }

        /// <summary>
        /// Create a new footnote and add it to the document.
        /// </summary>
        /// <remarks>
        /// The new note will have one paragraph with the style "FootnoteText"
        /// and one run containing the required footnote reference with the
        /// style "FootnoteReference".
        /// </remarks>
        /// <returns>New XWPFFootnote.</returns>
        public XWPFFootnote CreateFootnote()
        {
            XWPFFootnotes footnotes = this.CreateFootnotes();

            XWPFFootnote footnote = footnotes.CreateFootnote();
            return footnote;
        }
        /// <summary>
        /// Remove the specified footnote if present.
        /// </summary>
        /// <param name="pos"></param>
        /// <returns></returns>
        public bool RemoveFootnote(int pos)
        {
            if (null != footnotes)
            {
                return footnotes.RemoveFootnote(pos);
            }
            else
            {
                return false;
            }
        }

        /**
         * remove a BodyElement from bodyElements array list 
         * @param pos
         * @return true if removing was successfully, else return false
         */
        public bool RemoveBodyElement(int pos)
        {
            if (pos >= 0 && pos < bodyElements.Count)
            {
                BodyElementType type = bodyElements[(pos)].ElementType;
                if (type == BodyElementType.TABLE)
                {
                    int tablePos = GetTablePos(pos);
                    tables.RemoveAt(tablePos);
                    ctDocument.body.RemoveTbl(tablePos);
                }
                if (type == BodyElementType.PARAGRAPH)
                {
                    int paraPos = GetParagraphPos(pos);
                    paragraphs.RemoveAt(paraPos);
                    ctDocument.body.RemoveP(paraPos);
                 }
                bodyElements.RemoveAt(pos);
                return true;
            }
            return false;
        }

        /**
         * copies content of a paragraph to a existing paragraph in the list paragraphs at position pos
         * @param paragraph
         * @param pos
         */
        public void SetParagraph(XWPFParagraph paragraph, int pos)
        {
            paragraphs[pos]= paragraph;
            ctDocument.body.SetPArray(pos, paragraph.GetCTP());
            /* TODO update body element, update xwpf element, verify that
             * incoming paragraph belongs to this document or if not, XML was
             * copied properly (namespace-abbreviations, etc.)
             */
        }

        /**
         * @return the LastParagraph of the document
         */
        public XWPFParagraph GetLastParagraph()
        {
            int lastPos = paragraphs.ToArray().Length - 1;
            return paragraphs[(lastPos)];
        }

        /**
         * Create an empty table with one row and one column as default.
         * @return a new table
         */
        public XWPFTable CreateTable(int? pos = null)
        {
            XWPFTable table = new XWPFTable(ctDocument.body.AddNewTbl(pos), this);
            bodyElements.Add(table);
            tables.Add(table);
            return table;
        }

        /**
         * Create an empty table with a number of rows and cols specified
         * @param rows
         * @param cols
         * @return table
         */
        public XWPFTable CreateTable(int rows, int cols, int? pos = null)
        {
            XWPFTable table = new XWPFTable(ctDocument.body.AddNewTbl(pos), this, rows, cols);
            bodyElements.Add(table);
            tables.Add(table);
            return table;
        }

        /// <summary>
        /// Create a Table of Contents (TOC) at the end of the document.
        /// Please set paragraphs style to "Heading{#}" and document
        /// styles for TOC <see cref="DocumentStylesBuilder.BuildStylesForTOC"/>.
        /// Otherwise, it renders an empty one.
        /// </summary>
        public void CreateTOC()
        {
            var ctStyles = DocumentStylesBuilder.BuildStylesForTOC();
            styles.SetStyles(ctStyles);

            CT_SdtBlock tocBlock = Document.body.AddNewSdt();
            TOC toc = new TOC(tocBlock);
            toc.Build();

            EnforceUpdateFields(); // one time pop-up to update TOC when opening document
        }

        /**Replace content of table in array tables at position pos with a
         * @param pos
         * @param table
         */
        public void SetTable(int pos, XWPFTable table)
        {
            tables[pos] = table;
            ctDocument.body.SetTblArray(pos, table.GetCTTbl());
        }

        /**
         * Verifies that the documentProtection tag in settings.xml file <br/>
         * specifies that the protection is enforced (w:enforcement="1") <br/>
         * <br/>
         * sample snippet from settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;readOnly&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         *
         * @return true if documentProtection is enforced with option any
         */
        public bool IsEnforcedProtection()
        {
            return settings.IsEnforcedWith();
        }

        /**
         * Verifies that the documentProtection tag in Settings.xml file <br/>
         * specifies that the protection is enforced (w:enforcement="1") <br/>
         * and that the kind of protection is ReadOnly (w:edit="readOnly")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;readOnly&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         * 
         * @return true if documentProtection is enforced with option ReadOnly
         */
        public bool IsEnforcedReadonlyProtection()
        {
            return settings.IsEnforcedWith(ST_DocProtect.readOnly);
        }

        /**
         * Verifies that the documentProtection tag in Settings.xml file <br/>
         * specifies that the protection is enforced (w:enforcement="1") <br/>
         * and that the kind of protection is forms (w:edit="forms")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;forms&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         * 
         * @return true if documentProtection is enforced with option forms
         */
        public bool IsEnforcedFillingFormsProtection()
        {
            return settings.IsEnforcedWith(ST_DocProtect.forms);
        }

        /**
         * Verifies that the documentProtection tag in Settings.xml file <br/>
         * specifies that the protection is enforced (w:enforcement="1") <br/>
         * and that the kind of protection is comments (w:edit="comments")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;comments&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         * 
         * @return true if documentProtection is enforced with option comments
         */
        public bool IsEnforcedCommentsProtection()
        {
            return settings.IsEnforcedWith(ST_DocProtect.comments);
        }

        /**
         * Verifies that the documentProtection tag in Settings.xml file <br/>
         * specifies that the protection is enforced (w:enforcement="1") <br/>
         * and that the kind of protection is trackedChanges (w:edit="trackedChanges")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;trackedChanges&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         * 
         * @return true if documentProtection is enforced with option trackedChanges
         */
        public bool IsEnforcedTrackedChangesProtection()
        {
            return settings.IsEnforcedWith(ST_DocProtect.trackedChanges);
        }

        public bool IsEnforcedUpdateFields()
        {
            return settings.IsUpdateFields();
        }

        /**
         * Enforces the ReadOnly protection.<br/>
         * In the documentProtection tag inside Settings.xml file, <br/>
         * it Sets the value of enforcement to "1" (w:enforcement="1") <br/>
         * and the value of edit to ReadOnly (w:edit="readOnly")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;readOnly&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         */
        public void EnforceReadonlyProtection()
        {
            settings.SetEnforcementEditValue(ST_DocProtect.readOnly);
        }

        /**
         * Enforces the readOnly protection with a password.<br/>
         * <br/>
         * sample snippet from settings.xml
         * <pre>
         *   &lt;w:documentProtection w:edit=&quot;readOnly&quot; w:enforcement=&quot;1&quot; 
         *       w:cryptProviderType=&quot;rsaAES&quot; w:cryptAlgorithmClass=&quot;hash&quot;
         *       w:cryptAlgorithmType=&quot;typeAny&quot; w:cryptAlgorithmSid=&quot;14&quot;
         *       w:cryptSpinCount=&quot;100000&quot; w:hash=&quot;...&quot; w:salt=&quot;....&quot;
         *   /&gt;
         * </pre>
         * 
         * @param password the plaintext password, if null no password will be applied
         * @param hashAlgo the hash algorithm - only md2, m5, sha1, sha256, sha384 and sha512 are supported.
         *   if null, it will default default to sha1
         */
        public void EnforceReadonlyProtection(String password, HashAlgorithm hashAlgo)
        {
            settings.SetEnforcementEditValue(ST_DocProtect.readOnly, password, hashAlgo);
        }

        /**
         * Enforce the Filling Forms protection.<br/>
         * In the documentProtection tag inside Settings.xml file, <br/>
         * it Sets the value of enforcement to "1" (w:enforcement="1") <br/>
         * and the value of edit to forms (w:edit="forms")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;forms&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         */
        public void EnforceFillingFormsProtection()
        {
            settings.SetEnforcementEditValue(ST_DocProtect.forms);
        }

        /**
         * Enforce the Filling Forms protection.<br/>
         * <br/>
         * sample snippet from settings.xml
         * <pre>
         *   &lt;w:documentProtection w:edit=&quot;forms&quot; w:enforcement=&quot;1&quot; 
         *       w:cryptProviderType=&quot;rsaAES&quot; w:cryptAlgorithmClass=&quot;hash&quot;
         *       w:cryptAlgorithmType=&quot;typeAny&quot; w:cryptAlgorithmSid=&quot;14&quot;
         *       w:cryptSpinCount=&quot;100000&quot; w:hash=&quot;...&quot; w:salt=&quot;....&quot;
         *   /&gt;
         * </pre>
         * 
         * @param password the plaintext password, if null no password will be applied
         * @param hashAlgo the hash algorithm - only md2, m5, sha1, sha256, sha384 and sha512 are supported.
         *   if null, it will default default to sha1
         */
        public void EnforceFillingFormsProtection(String password, HashAlgorithm hashAlgo)
        {
            settings.SetEnforcementEditValue(ST_DocProtect.forms, password, hashAlgo);
        }

        /**
         * Enforce the Comments protection.<br/>
         * In the documentProtection tag inside Settings.xml file,<br/>
         * it Sets the value of enforcement to "1" (w:enforcement="1") <br/>
         * and the value of edit to comments (w:edit="comments")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;comments&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         */
        public void EnforceCommentsProtection()
        {
            settings.SetEnforcementEditValue(ST_DocProtect.comments);
        }

        /**
         * Enforce the Comments protection.<br/>
         * <br/>
         * sample snippet from settings.xml
         * <pre>
         *   &lt;w:documentProtection w:edit=&quot;comments&quot; w:enforcement=&quot;1&quot; 
         *       w:cryptProviderType=&quot;rsaAES&quot; w:cryptAlgorithmClass=&quot;hash&quot;
         *       w:cryptAlgorithmType=&quot;typeAny&quot; w:cryptAlgorithmSid=&quot;14&quot;
         *       w:cryptSpinCount=&quot;100000&quot; w:hash=&quot;...&quot; w:salt=&quot;....&quot;
         *   /&gt;
         * </pre>
         * 
         * @param password the plaintext password, if null no password will be applied
         * @param hashAlgo the hash algorithm - only md2, m5, sha1, sha256, sha384 and sha512 are supported.
         *   if null, it will default default to sha1
         */
        public void EnforceCommentsProtection(String password, HashAlgorithm hashAlgo)
        {
            settings.SetEnforcementEditValue(ST_DocProtect.comments, password, hashAlgo);
        }

        /**
         * Enforce the Tracked Changes protection.<br/>
         * In the documentProtection tag inside Settings.xml file, <br/>
         * it Sets the value of enforcement to "1" (w:enforcement="1") <br/>
         * and the value of edit to trackedChanges (w:edit="trackedChanges")<br/>
         * <br/>
         * sample snippet from Settings.xml
         * <pre>
         *     &lt;w:settings  ... &gt;
         *         &lt;w:documentProtection w:edit=&quot;trackedChanges&quot; w:enforcement=&quot;1&quot;/&gt;
         * </pre>
         */
        public void EnforceTrackedChangesProtection()
        {
            settings.SetEnforcementEditValue(ST_DocProtect.trackedChanges);
        }

        /**
         * Enforce the Tracked Changes protection.<br/>
         * <br/>
         * sample snippet from settings.xml
         * <pre>
         *   &lt;w:documentProtection w:edit=&quot;trackedChanges&quot; w:enforcement=&quot;1&quot; 
         *       w:cryptProviderType=&quot;rsaAES&quot; w:cryptAlgorithmClass=&quot;hash&quot;
         *       w:cryptAlgorithmType=&quot;typeAny&quot; w:cryptAlgorithmSid=&quot;14&quot;
         *       w:cryptSpinCount=&quot;100000&quot; w:hash=&quot;...&quot; w:salt=&quot;....&quot;
         *   /&gt;
         * </pre>
         * 
         * @param password the plaintext password, if null no password will be applied
         * @param hashAlgo the hash algorithm - only md2, m5, sha1, sha256, sha384 and sha512 are supported.
         *   if null, it will default default to sha1
         */
        public void EnforceTrackedChangesProtection(String password, HashAlgorithm hashAlgo)
        {
            settings.SetEnforcementEditValue(ST_DocProtect.trackedChanges, password, hashAlgo);
        }

        /**
         * Validates the existing password
         *
         * @param password
         * @return true, only if password was set and equals, false otherwise
         */
        public bool ValidateProtectionPassword(String password)
        {
            return settings.ValidateProtectionPassword(password);
        }

        /**
         * Remove protection enforcement.<br/>
         * In the documentProtection tag inside Settings.xml file <br/>
         * it Sets the value of enforcement to "0" (w:enforcement="0") <br/>
         */
        public void RemoveProtectionEnforcement()
        {
            settings.RemoveEnforcement();
        }

        /**
         * Enforces fields update on document open (in Word).
         * In the settings.xml file <br/>
         * sets the updateSettings value to true (w:updateSettings w:val="true")
         * 
         *  NOTICES:
         *  <ul>
         *  	<li>Causing Word to ask on open: "This document contains fields that may refer to other files. Do you want to update the fields in this document?"
         *           (if "Update automatic links at open" is enabled)</li>
         *  	<li>Flag is removed after saving with changes in Word </li>
         *  </ul> 
         */
        public void EnforceUpdateFields()
        {
            settings.SetUpdateFields();
        }

        /**
          * Check if revision tracking is turned on.
          * 
          * @return <code>true</code> if revision tracking is turned on
          */
        public bool IsTrackRevisions
        {
            get
            {
                return settings.IsTrackRevisions;
            }
            set
            {
                settings.IsTrackRevisions = value;
            }
        }

        /**
         * inserts an existing XWPFTable to the arrays bodyElements and tables
         * @param pos
         * @param table
         */
        public void InsertTable(int pos, XWPFTable table)
        {
            bodyElements.Insert(pos, table);
            int i;
            CT_Tbl[] barray = ctDocument.body.GetTblArray();
            for (i = 0; i < barray.Length; i++)
            {
                //CT_Tbl tbl = ctDocument.body.GetTblArray(i);
                if (barray[i] == table.GetCTTbl())
                {
                    break;
                }
            }
            tables.Insert(i, table);
        }

        /**
         * Returns all Pictures, which are referenced from the document itself.
         * @return a {@link List} of {@link XWPFPictureData}. The returned {@link List} is unmodifiable. Use #a
         */
        public IList<XWPFPictureData> AllPictures
        {
            get
            {
                return pictures.AsReadOnly();
            }
        }

        /**
         * @return all Pictures in this package
         */
        public IList<XWPFPictureData> AllPackagePictures
        {
            get
            {
                List<XWPFPictureData> result = new List<XWPFPictureData>();
                //List<XWPFPictureData> values = packagePictures.Values(;
                foreach (List<XWPFPictureData> list in packagePictures.Values)
                {
                    result.AddRange(list);
                }
                return result.AsReadOnly();
            }
        }

        public void RegisterPackagePictureData(XWPFPictureData picData)
        {
            List<XWPFPictureData> list = null;
            if(packagePictures.TryGetValue(picData.Checksum, out List<XWPFPictureData> picture))
              list = picture;
            if (list == null)
            {
                list = new List<XWPFPictureData>(1);
                packagePictures.Add(picData.Checksum, list);
            }
            if (!list.Contains(picData))
            {
                list.Add(picData);
            }
        }

        public XWPFPictureData FindPackagePictureData(byte[] pictureData, int format)
        {
            long Checksum = IOUtils.CalculateChecksum(pictureData);
            XWPFPictureData xwpfPicData = null;
            /*
             * Try to find PictureData with this Checksum. Create new, if none
             * exists.
             */

            if (packagePictures.TryGetValue(Checksum, out List<XWPFPictureData> xwpfPicDataList))
            {
                foreach (XWPFPictureData curElem in xwpfPicDataList)
                {
                    if (Arrays.Equals(pictureData, curElem.Data))
                    {
                        xwpfPicData = curElem;
                        break;
                    }
                }
            }
            return xwpfPicData;

        }

        public String AddPictureData(byte[] pictureData, int format)
        {
            XWPFPictureData xwpfPicData = FindPackagePictureData(pictureData, format);
            POIXMLRelation relDesc = XWPFPictureData.RELATIONS[format];

            if (xwpfPicData == null)
            {
                /* Part doesn't exist, create a new one */
                int idx = GetNextPicNameNumber(format);
                xwpfPicData = (XWPFPictureData)CreateRelationship(relDesc, XWPFFactory.GetInstance(), idx);
                /* write bytes to new part */
                PackagePart picDataPart = xwpfPicData.GetPackagePart();
                Stream out1 = null;
                try
                {
                    out1 = picDataPart.GetOutputStream();
                    out1.Write(pictureData, 0, pictureData.Length);
                }
                catch (IOException e)
                {
                    throw new POIXMLException(e);
                }
                finally
                {
                    try
                    {
                        if (out1 != null)
                            out1.Close();
                    }
                    catch (IOException)
                    {
                        // ignore
                    }
                }

                RegisterPackagePictureData(xwpfPicData);
                pictures.Add(xwpfPicData);

                return GetRelationId(xwpfPicData);
            }
            else if (!GetRelations().Contains(xwpfPicData))
            {
                /*
                 * Part already existed, but was not related so far. Create
                 * relationship to the already existing part and update
                 * POIXMLDocumentPart data.
                 */
                // TODO add support for TargetMode.EXTERNAL relations.
                RelationPart rp = AddRelation(null, XWPFRelation.IMAGES, xwpfPicData);
                return rp.Relationship.Id;
            }
            else
            {
                /* Part already existed, Get relation id and return it */
                return GetRelationId(xwpfPicData);
            }
        }

        public String AddPictureData(Stream is1, int format)
        {
            try
            {
                byte[] data = IOUtils.ToByteArray(is1);
                return AddPictureData(data, format);
            }
            catch (IOException e)
            {
                throw new POIXMLException(e);
            }
        }

        /**
         * Get the next free ImageNumber
         * @param format
         * @return the next free ImageNumber
         * @throws InvalidFormatException 
         */
        public int GetNextPicNameNumber(int format)
        {
            int img = AllPackagePictures.Count + 1;
            String proposal = XWPFPictureData.RELATIONS[format].GetFileName(img);
            PackagePartName CreatePartName = PackagingUriHelper.CreatePartName(proposal);
            while (this.Package.GetPart(CreatePartName) != null) {
                img++;
                proposal = XWPFPictureData.RELATIONS[format].GetFileName(img);
                CreatePartName = PackagingUriHelper.CreatePartName(proposal);
            }
            return img;
        }

        /**
         * returns the PictureData by blipID
         * @param blipID
         * @return XWPFPictureData of a specificID
         */
        public XWPFPictureData GetPictureDataByID(String blipID)
        {
            POIXMLDocumentPart relatedPart = GetRelationById(blipID);
            if (relatedPart is XWPFPictureData xwpfPicData)
            {
                return xwpfPicData;
            }
            return null;
        }

        /**
         * GetNumbering
         * @return numbering
         */
        public XWPFNumbering GetNumbering()
        {
            if (numbering == null)
                numbering = new XWPFNumbering();
            return numbering;
        }

        /**
         * Get Styles
         * @return styles for this document
         */
        public XWPFStyles GetStyles()
        {
            return styles;
        }

        /**
         * Get the paragraph with the CTP class p
         * 
         * @param p
         * @return the paragraph with the CTP class p
         */
        public XWPFParagraph GetParagraph(CT_P p)
        {
            for (int i = 0; i < Paragraphs.Count; i++)
            {
                if (Paragraphs[(i)].GetCTP() == p)
                {
                    return Paragraphs[(i)];
                }
            }
            return null;
        }

        /**
         * Get a table by its CTTbl-Object
         * @param ctTbl
         * @see NPOI.XWPF.UserModel.IBody#getTable(org.Openxmlformats.schemas.wordProcessingml.x2006.main.CTTbl)
         * @return a table by its CTTbl-Object or null
         */
        public XWPFTable GetTable(CT_Tbl ctTbl)
        {
            for (int i = 0; i < tables.Count; i++)
            {
                if (tables[i].GetCTTbl() == ctTbl)
                {
                    return tables[i];
                }
            }
            return null;
        }


        public IEnumerator<XWPFTable> GetTablesEnumerator()
        {
            return tables.GetEnumerator();
        }
        /// <summary>
        /// Change orientation of a Word file
        /// </summary>
        /// <param name="orientation"></param>
        /// <remarks>https://stackoverflow.com/questions/26483837/landscape-and-portrait-pages-in-the-same-word-document-using-apache-poi-xwpf-in</remarks>
        public void ChangeOrientation(ST_PageOrientation orientation)
        {
            var body = this.Document.body;
            if (body.sectPr == null)
            {
                body.AddNewSectPr();
            }
            var section = body.sectPr;
            XWPFParagraph para = this.CreateParagraph();
            var ctp = para.GetCTP();
            var br = ctp.AddNewPPr();
            br.sectPr = section;
            var pageSize = section.pgSz;
            pageSize.orient = orientation;
            if (orientation== ST_PageOrientation.landscape)
            {
                pageSize.w = 842 * 20;
                pageSize.h = 595 * 20;
            }
            else
            {
                pageSize.h = 842 * 20;
                pageSize.w = 595 * 20;
            }
        }

        public IEnumerator<XWPFParagraph> GetParagraphsEnumerator()
        {
            return paragraphs.GetEnumerator();
        }

        /**
         * Returns the paragraph that of position pos
         * @see NPOI.XWPF.UserModel.IBody#getParagraphArray(int)
         */
        public XWPFParagraph GetParagraphArray(int pos)
        {
            if (pos >= 0 && pos < paragraphs.Count)
            {
                return paragraphs[(pos)];
            }
            return null;
        }

        /**
         * returns the Part, to which the body belongs, which you need for Adding relationship to other parts
         * Actually it is needed of the class XWPFTableCell. Because you have to know to which part the tableCell
         * belongs.
         * @see NPOI.XWPF.UserModel.IBody#getPart()
         */
        public POIXMLDocumentPart Part
        {
            get
            {
                return this;
            }
        }


        /**
         * Get the PartType of the body, for example
         * DOCUMENT, HEADER, FOOTER,	FOOTNOTE,
         *
         * @see NPOI.XWPF.UserModel.IBody#getPartType()
         */
        public BodyType PartType
        {
            get
            {
                return BodyType.DOCUMENT;
            }
        }

        /**
         * Get the TableCell which belongs to the TableCell
         * @param cell
         */
        public XWPFTableCell GetTableCell(CT_Tc cell)
        {
            if (cell == null|| cell.Parent is not CT_Row row)
                return null;

            object parent2 = row.Parent;
            if ( parent2== null || parent2 is not CT_Tbl tbl)
                return null;
            XWPFTable table = GetTable(tbl);
            if (table == null)
            {
                return null;
            }
            XWPFTableRow tableRow = table.GetRow(row);
            if (tableRow == null)
            {
                return null;
            }
            return tableRow.GetTableCell(cell);
        }

        public XWPFDocument GetXWPFDocument()
        {
            return this;
        }

        private static void FindAndReplaceTextInParagraph(XWPFParagraph paragraph, string oldValue, string newValue, int startPos = 0)
        {
            if(paragraph == null)
                return;

            string paragraphText = string.Concat(paragraph.Runs.Select(p => p.Text));

            var startIndex = paragraphText.IndexOf(oldValue, startPos);
            if(startIndex == -1)
                return;

            int firstRun = -1;
            int firstIndex = -1;

            int lastRun = -1;
            int lastIndex = -1;

            int processedRuns = 0;
            int processedChars = 0;

            for(; processedRuns < paragraph.Runs.Count; processedRuns++)
            {
                var text = paragraph.Runs[processedRuns].Text;
                if(processedChars + text.Length > startIndex)
                {
                    firstRun = processedRuns;
                    firstIndex = startIndex - processedChars;
                    break;
                }

                processedChars += text.Length;
            }

            int endIndex = startIndex + oldValue.Length;

            for(; processedRuns < paragraph.Runs.Count; processedRuns++)
            {
                var text = paragraph.Runs[processedRuns].Text;
                if(processedChars + text.Length > endIndex)
                {
                    lastRun = processedRuns;
                    lastIndex = endIndex - processedChars;
                    break;
                }

                processedChars += text.Length;
            }

            var initialFirstText = paragraph.Runs[firstRun].Text;
            if(firstRun == lastRun)
            {
                paragraph.Runs[firstRun].SetText(initialFirstText.Substring(0, firstIndex) + newValue + initialFirstText.Substring(lastIndex));
            }
            else
            {
                paragraph.Runs[firstRun].SetText(initialFirstText.Substring(0, firstIndex) + newValue);

                if(lastRun != -1)
                    paragraph.Runs[lastRun].SetText(paragraph.Runs[lastRun].Text.Substring(lastIndex));
            }

            int removeTo = lastRun == -1 ? paragraph.Runs.Count : lastRun;
            for(int i = firstRun + 1; i < removeTo; i++)
                paragraph.RemoveRun(firstRun + 1);

            FindAndReplaceTextInParagraph(paragraph, oldValue, newValue, startIndex + newValue.Length);
        }

        private static void FindAndReplaceTextInTable(XWPFTable table, string oldValue, string newValue)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.GetTableCells())
                {
                    foreach (var innerTable in cell.Tables)
                    {
                        FindAndReplaceTextInTable(innerTable, oldValue, newValue);
                    }
                    foreach (var paragraph in cell.Paragraphs)
                    {
                        FindAndReplaceTextInParagraph(paragraph, oldValue, newValue);
                    }
                }
            }
        }

        public void FindAndReplaceText(string oldValue, string newValue)
        {
            foreach (var paragraph in this.Paragraphs)
            {
                FindAndReplaceTextInParagraph(paragraph, oldValue, newValue);
            }

            foreach (var table in this.Tables)
            {
                FindAndReplaceTextInTable(table, oldValue, newValue);
            }
            
            foreach (var footer in this.FooterList)
            {
                foreach (var paragraph in footer.Paragraphs)
                {
                    FindAndReplaceTextInParagraph(paragraph, oldValue, newValue);
                }
                foreach(var table in footer.Tables)
                {
                    FindAndReplaceTextInTable(table, oldValue, newValue);
                }
            }

            foreach (var header in this.HeaderList)
            {
                foreach (var paragraph in header.Paragraphs)
                {
                    FindAndReplaceTextInParagraph(paragraph, oldValue, newValue);
                }
                foreach(var table in header.Tables)
                {
                    FindAndReplaceTextInTable(table, oldValue, newValue);
                }
            }
        }
        public void Dispose()
        {
            this.Close();
        }
    }
}
