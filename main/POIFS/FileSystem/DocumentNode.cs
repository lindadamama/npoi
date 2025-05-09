﻿/* ====================================================================
   Licensed to the Apache Software Foundation (ASF) under one or more
   contributor license agreements.  See the NOTICE file distributed with
   this work for additional information regarding copyright ownership.
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

using System;
using System.Collections.Generic;

using NPOI.POIFS.FileSystem;
using NPOI.POIFS.Properties;
using NPOI.POIFS.Dev;

namespace NPOI.POIFS.FileSystem
{
    /// <summary>
    /// Simple implementation of DocumentEntry
    /// @author Marc Johnson (mjohnson at apache dot org)
    /// </summary>
    public class DocumentNode : EntryNode, POIFSViewable, DocumentEntry
    {
        // underlying POIFSDocument instance
        private readonly OPOIFSDocument _document;

        /**
         * create a DocumentNode. This method Is not public by design; it
         * Is intended strictly for the internal use of this package
         *
         * @param property the DocumentProperty for this DocumentEntry
         * @param parent the parent of this entry
         */

        public DocumentNode(DocumentProperty property, DirectoryNode parent):base(property, parent)
        {
            _document = property.Document;
        }

        /**
         * get the POIFSDocument
         *
         * @return the internal POIFSDocument
         */

        public OPOIFSDocument Document
        {
            get { return _document; }
        }


        /**
         * get the zize of the document, in bytes
         *
         * @return size in bytes
         */

        public int Size
        {
            get { return Property.Size; }
        }


        /**
         * Is this a DocumentEntry?
         *
         * @return true if the Entry Is a DocumentEntry, else false
         */

        public override bool IsDocumentEntry
        {
            get{return true;}
        }


        /**
         * extensions use this method to verify internal rules regarding
         * deletion of the underlying store.
         *
         * @return true if it's ok to delete the underlying store, else
         *         false
         */

        protected override bool IsDeleteOK
        {
            get { return true; }
        }


        /**
         * Get an array of objects, some of which may implement
         * POIFSViewable
         *
         * @return an array of Object; may not be null, but may be empty
         */

        public Object[] ViewableArray
        {
            get { return []; }
        }

        /**
         * Get an Iterator of objects, some of which may implement
         * POIFSViewable
         *
         * @return an Iterator; may not be null, but may have an empty
         * back end store
         */

        public IEnumerator<Object> ViewableIterator
        {
            get
            {
                List<Object> components = new List<Object>();

                components.Add(Property);
                if (_document != null)
                {
                    components.Add(_document);
                }
                return components.GetEnumerator();
            }
        }

        /**
         * Give viewers a hint as to whether to call getViewableArray or
         * getViewableIterator
         *
         * @return true if a viewer should call getViewableArray, false if
         *         a viewer should call getViewableIterator
         */

        public bool PreferArray
        {
            get { return false; }
        }

        /**
         * Provides a short description of the object, to be used when a
         * POIFSViewable object has not provided its contents.
         *
         * @return short description
         */

        public String ShortDescription
        {
            get{return Name;}
        }
    }
}
