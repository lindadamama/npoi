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

using System.Collections.Generic;

using NPOI.HSSF.Model;

namespace NPOI.HSSF.Record.Aggregates
{
    /// <summary>
    /// Manages the DVALRecord and DVRecords for a single sheet
    /// See OOO excelfileformat.pdf section 4.14
    /// @author Josh Micich
    /// </summary>
    public class DataValidityTable : RecordAggregate
    {

        private readonly DVALRecord _headerRec;
        /**
         * The list of data validations for the current sheet.
         * Note - this may be empty (contrary to OOO documentation)
         */
        private readonly List<DVRecord> _validationList;

        public DataValidityTable(RecordStream rs)
        {
            _headerRec = (DVALRecord)rs.GetNext();
            List<DVRecord> temp = new();
            while (rs.PeekNextClass() == typeof(DVRecord))
            {
                temp.Add((DVRecord) rs.GetNext());
            }
            _validationList = temp;
        }

        public DataValidityTable()
        {
            _headerRec = new DVALRecord();
            _validationList = new List<DVRecord>();
        }

        public override void VisitContainedRecords(RecordVisitor rv)
        {
            if (_validationList.Count == 0)
            {
                return;
            }
            rv.VisitRecord(_headerRec);
            for (int i = 0; i < _validationList.Count; i++)
            {
                rv.VisitRecord((Record)_validationList[i]);
            }
        }

        public void AddDataValidation(DVRecord dvRecord)
        {
            _validationList.Add(dvRecord);
            _headerRec.DVRecNo = (_validationList.Count);
        }

        public void RemoveDataValidation(DVRecord dvRecord)
        {
            _validationList.Remove(dvRecord);
            _headerRec.DVRecNo = _validationList.Count;
        }
    }
}
