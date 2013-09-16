﻿using AngleSharp.Xml;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AngleSharp.DTD
{
    /// <summary>
    /// The tokenizer class for Document Type Definitions.
    /// </summary>
    [DebuggerStepThrough]
    sealed class DtdTokenizer : BaseTokenizer
    {
        #region Constants

        const String ENTITY = "ENTITY";
        const String ELEMENT = "ELEMENT";
        const String NOTATION = "NOTATION";
        const String ATTLIST = "ATTLIST";
        const String EMPTY = "EMPTY";
        const String ANY = "ANY";
        const String PCDATA = "#PCDATA";
        const String NDATA = "NDATA";
        const String CDATA = "CDATA";
        const String ID = "ID";
        const String IDREF = "IDREF";
        const String IDREFS = "IDREFS";
        const String ENTITIES = "ENTITIES";
        const String NMTOKEN = "NMTOKEN";
        const String NMTOKENS = "NMTOKENS";
        const String REQUIRED = "#REQUIRED";
        const String IMPLIED = "#IMPLIED";
        const String FIXED = "#FIXED";
        const String PUBLIC = "PUBLIC";
        const String SYSTEM = "SYSTEM";
        const String INCLUDE = "INCLUDE";
        const String IGNORE = "IGNORE";

        #endregion

        #region Members

        Boolean _external;
        Char _endChar;
        IntermediateStream _stream;
        DtdContainer _container;
        Int32 _includes;

        #endregion

        #region ctor

        /// <summary>
        /// Creates a new DTD tokenizer with the given source and container.
        /// </summary>
        /// <param name="container">The container to use.</param>
        /// <param name="src">The source to inspect.</param>
        public DtdTokenizer(DtdContainer container, SourceManager src)
            : base(src)
        {
            _stream = new IntermediateStream(src);
            _container = container;
            _includes = 0;
            IsExternal = true;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the parsed content.
        /// </summary>
        public String Content
        {
            get { return _stream.Content; }
        }

        /// <summary>
        /// Gets or sets if the DTD is from an external source.
        /// </summary>
        public Boolean IsExternal 
        {
            get { return _external; }
            set
            {
                _external = value;
                _endChar = _external ? Specification.EOF : Specification.SBC;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Scans the DTD in the doctype as specified in the
        /// official XML spec and (in German) here:
        /// http://www.stefanheymann.de/xml/dtdxml.htm
        /// </summary>
        public DtdToken Get()
        {
            var c = _stream.Current;
            var element = GetElement(c);

            if (element != DtdToken.EOF)
                SkipSpaces(c);

            return element;
        }

        #endregion

        #region General

        /// <summary>
        /// Gets the next found DTD element by advancing
        /// and applying the rules for DTD.
        /// </summary>
        /// <param name="c">The current character.</param>
        /// <returns>The found declaration.</returns>
        DtdToken GetElement(Char c)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == _endChar && _includes == 0)
                return DtdToken.EOF;
            
            if (c == Specification.SBC)
            {
                if (_includes > 0 && _stream.Next == Specification.SBC && _stream.Next == Specification.GT)
                {
                    _includes--;
                    return GetElement(_stream.Next);
                }
            }
            else if (c == Specification.LT)
            {
                c = _stream.Next;

                if (c == Specification.QM)
                {
                    return ProcessingStart(_stream.Next);
                }
                else if (c == Specification.EM)
                {
                    _stream.Advance();

                    if (_stream.ContinuesWith(ENTITY))
                    {
                        _stream.Advance(5);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return EntityDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(ELEMENT))
                    {
                        _stream.Advance(6);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return TypeDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(ATTLIST))
                    {
                        _stream.Advance(6);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return AttributeDeclaration(c);
                    }
                    else if (_stream.ContinuesWith(NOTATION))
                    {
                        _stream.Advance(7);
                        c = _stream.Next;

                        if (c.IsSpaceCharacter())
                            return NotationDeclaration(c);
                    }
                    else if (_stream.ContinuesWith("--"))
                    {
                        _stream.Advance();
                        return CommentStart(_stream.Next);
                    }
                    else if (_stream.Current == Specification.SBO && _external)
                        return Conditional(_stream.Next);
                }
            }
            else if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return GetElement(_stream.Current);
            }

            throw Errors.Xml(ErrorCode.DtdInvalid);
        }

        #endregion

        #region Conditional

        /// <summary>
        /// Treats the conditional sects with respect.
        /// http://www.w3.org/TR/REC-xml/#sec-condition-sect
        /// </summary>
        /// <param name="c">The current character.</param>
        /// <returns>The evaluated token.</returns>
        DtdToken Conditional(Char c)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (_stream.ContinuesWith(INCLUDE))
            {
                _stream.Advance(6);

                do c = _stream.Next;
                while (c.IsSpaceCharacter());

                if (c == Specification.SBO)
                {
                    _includes++;
                    return GetElement(_stream.Next);
                }
            }
            else if (_stream.ContinuesWith(IGNORE))
            {
                _stream.Advance(5);

                do c = _stream.Next;
                while (c.IsSpaceCharacter());

                if (c == Specification.SBO)
                {
                    var nesting = 0;
                    var lastThree = new[] { Specification.NULL, Specification.NULL, Specification.NULL };

                    do
                    {
                        c = _stream.Next;

                        if (c == Specification.EOF)
                            break;

                        lastThree[0] = lastThree[1];
                        lastThree[1] = lastThree[2];
                        lastThree[2] = c;

                        if (lastThree[0] == Specification.LT && lastThree[1] == Specification.EM && lastThree[2] == Specification.SBO)
                            nesting++;
                    }
                    while (nesting != 0 || lastThree[0] != Specification.SBC || lastThree[1] != Specification.SBC || lastThree[2] != Specification.GT);

                    if (c == Specification.GT)
                        return GetElement(_stream.Next);
                }
            }

            throw Errors.Xml(ErrorCode.DtdConditionInvalid);
        }

        #endregion

        #region Processing Instruction

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken ProcessingStart(Char c)
        {
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return ProcessingTarget(_stream.Next, new DtdPIToken());
            }

            throw Errors.Xml(ErrorCode.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        DtdToken ProcessingTarget(Char c, DtdPIToken pi)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            pi.Target = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if (String.Compare(pi.Target, Tags.XML, StringComparison.OrdinalIgnoreCase) == 0)
                return TextDecl(c);
            
            if (c == Specification.QM)
            {
                c = _stream.Next;

                if(c == Specification.GT)
                    return pi;
            }
            else if (c.IsSpaceCharacter())
                return ProcessingContent(_stream.Next, pi);

            throw Errors.Xml(ErrorCode.XmlInvalidPI);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-pi.
        /// </summary>
        /// <param name="c">The next input character.</param>
        /// <param name="pi">The processing instruction token.</param>
        DtdToken ProcessingContent(Char c, DtdPIToken pi)
        {
            while (c != Specification.EOF)
            {
                if (c == Specification.QM)
                {
                    c = _src.Next;

                    if (c == Specification.GT)
                    {
                        pi.Content = _stringBuffer.ToString();
                        return pi;
                    }

                    _stringBuffer.Append(Specification.QM);
                }
                else
                {
                    _stringBuffer.Append(c);
                    c = _src.Next;
                }
            }

            throw Errors.Xml(ErrorCode.EOF);
        }

        #endregion

        #region Text Declaration

        /// <summary>
        /// The text declaration for external DTDs.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns>The token.</returns>
        DtdToken TextDecl(Char c)
        {
            if (IsExternal)
            {
                var token = new DtdDeclToken();

                if (c.IsSpaceCharacter())
                {
                    c = SkipSpaces(c);

                    if (_stream.ContinuesWith(AttributeNames.VERSION))
                    {
                        _stream.Advance(6);
                        return TextDeclVersion(_stream.Next, token);
                    }
                    else if (_stream.ContinuesWith(AttributeNames.ENCODING))
                    {
                        _stream.Advance(7);
                        return TextDeclEncoding(_stream.Next, token);
                    }
                }
            }

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        /// <summary>
        /// Gets the version specified in the text declaration.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="decl">The current declaration.</param>
        /// <returns>The token.</returns>
        DtdToken TextDeclVersion(Char c, DtdDeclToken decl)
        {
            if (c == Specification.EQ)
            {
                var q = _stream.Next;

                if (q == Specification.DQ || q == Specification.SQ)
                {
                    _stringBuffer.Clear();
                    c = _stream.Next;

                    while (c.IsDigit() || c == Specification.DOT)
                    {
                        _stringBuffer.Append(c);
                        c = _stream.Next;
                    }

                    if (c == q)
                    {
                        decl.Version = _stringBuffer.ToString();
                        return TextDeclBetween(_stream.Next, decl);
                    }
                }
            }

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        /// <summary>
        /// Between the version and the encoding in the text declaration.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="decl">The current declaration.</param>
        /// <returns>The token.</returns>
        DtdToken TextDeclBetween(Char c, DtdDeclToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (_stream.ContinuesWith(AttributeNames.ENCODING))
                {
                    _stream.Advance(7);
                    return TextDeclEncoding(_stream.Next, decl);
                }
            }

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        /// <summary>
        /// Gets the encoding specified in the text declaration.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="decl">The current declaration.</param>
        /// <returns>The token.</returns>
        DtdToken TextDeclEncoding(Char c, DtdDeclToken decl)
        {
            if (c == Specification.EQ)
            {
                var q = _stream.Next;

                if (q == Specification.DQ || q == Specification.SQ)
                {
                    _stringBuffer.Clear();
                    c = _stream.Next;

                    if (c.IsLetter())
                    {
                        do
                        {
                            _stringBuffer.Append(c);
                            c = _stream.Next;
                        }
                        while (c.IsAlphanumericAscii() || c == Specification.DOT || c == Specification.UNDERSCORE || c == Specification.MINUS);
                    }

                    if (c == q)
                    {
                        decl.Encoding = _stringBuffer.ToString();
                        return TextDeclAfter(_stream.Next, decl);
                    }
                }
            }

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        /// <summary>
        /// After the declaration specified in the text declaration.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="decl">The current declaration.</param>
        /// <returns>The token.</returns>
        DtdToken TextDeclAfter(Char c, DtdDeclToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.QM)
                return TextDeclEnd(_stream.Next, decl);

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        /// <summary>
        /// Checks if the text declaration ended correctly.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <param name="decl">The current declaration.</param>
        /// <returns>The token.</returns>
        DtdToken TextDeclEnd(Char c, DtdDeclToken decl)
        {
            if(c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdTextDeclInvalid);
        }

        #endregion

        #region Comments

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentStart(Char c)
        {
            _stringBuffer.Clear();
            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken Comment(Char c)
        {
            while (c.IsXmlChar())
            {
                if (c == Specification.MINUS)
                    return CommentDash(_stream.Next);

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            throw Errors.Xml(ErrorCode.XmlInvalidComment);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentDash(Char c)
        {
            if (c == Specification.MINUS)
                return CommentEnd(_stream.Next);

            return Comment(c);
        }

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-comments.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken CommentEnd(Char c)
        {
            if (c == Specification.GT)
                return new DtdCommentToken() { Data = _stringBuffer.ToString() };

            throw Errors.Xml(ErrorCode.XmlInvalidComment);
        }

        #endregion

        #region Declaration Name

        Boolean DeclarationNameBefore(Char c, DtdToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.EOF)
                throw Errors.Xml(ErrorCode.EOF);

            if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return DeclarationNameBefore(_stream.Current, decl);
            }
            
            if (c.IsXmlNameStart())
            {
                _stringBuffer.Clear();
                _stringBuffer.Append(c);
                return DeclarationName(_stream.Next, decl);
            }

            return false;
        }

        Boolean DeclarationName(Char c, DtdToken decl)
        {
            while (c.IsXmlName())
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            if (c == Specification.PERCENT)
            {
                PEReference(_stream.Next);
                return DeclarationName(_stream.Current, decl);
            }

            decl.Name = _stringBuffer.ToString();
            _stringBuffer.Clear();

            if (c == Specification.EOF)
                throw Errors.Xml(ErrorCode.EOF);

            return c.IsSpaceCharacter();
        }

        #endregion

        #region References

        void PEReference(Char c, Boolean use = true)
        {
            var buffer = Pool.NewStringBuilder();

            if (c.IsXmlNameStart())
            {
                do
                {
                    buffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                var temp = buffer.ToPool();

                if (c == Specification.SC)
                {
                    var p = _container.GetParameter(temp);

                    if (p != null)
                    {
                        if (use)
                        {
                            _stream.Push(temp, p.NodeValue);
                            return;
                        }
                        else
                            throw Errors.Xml(ErrorCode.DtdPEReferenceInvalid);
                    }
                }
            }

            if (use)
                throw Errors.Xml(ErrorCode.DtdPEReferenceInvalid);
            
            _stringBuffer.Append(Specification.PERCENT).Append(buffer.ToString());
        }

        String EReference(Char c)
        {
            var buffer = Pool.NewStringBuilder();

            if (c.IsXmlNameStart())
            {
                do
                {
                    buffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                var temp = buffer.ToPool();

                if (temp.Length > 0 && c == Specification.SC)
                {
                    var p = _container.GetEntity(temp);

                    if (p != null)
                        return p.NodeValue;
                }
            }
            else if (c == Specification.NUM)
            {
                c = _src.Next;
                var hex = c == 'x' || c == 'X';

                if (hex)
                    c = _stream.Next;

                while (hex ? c.IsHex() : c.IsDigit())
                {
                    buffer.Append(c);
                    c = _src.Next;
                }

                var temp = buffer.ToPool();

                if (temp.Length > 0 && c == Specification.SC)
                {
                    var num = hex ? temp.FromHex() : temp.FromDec();

                    if (num.IsValidAsCharRef())
                        return Char.ConvertFromUtf32(num);

                    throw Errors.Xml(ErrorCode.CharacterReferenceInvalidNumber);
                }
            }

            throw Errors.Xml(ErrorCode.CharacterReferenceNotTerminated);
        }

        #endregion

        #region Entity Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#sec-entity-decl.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdToken EntityDeclaration(Char c)
        {
            var decl = new DtdEntityToken();

            if (c.IsSpaceCharacter())
                c = SkipSpaces(c);

            if (c == Specification.PERCENT)
            {
                decl.IsParameter = true;

                if (!_stream.Next.IsSpaceCharacter())
                    throw Errors.Xml(ErrorCode.CharacterReferenceInvalidCode);

                c = SkipSpaces(c);
            }

            if (DeclarationNameBefore(c, decl))
            {
                c = SkipSpaces(c);

                if (_stream.ContinuesWith(SYSTEM))
                {
                    decl.IsExtern = true;
                    _stream.Advance(5);
                    return EntityDeclarationBeforeValue(_stream.Next, decl);
                }
                else if (_stream.ContinuesWith(PUBLIC))
                {
                    decl.IsExtern = true;
                    _stream.Advance(5);
                    return EntityDeclarationBeforeSystem(_stream.Next, decl);
                }
                else if (Specification.DQ == c || Specification.SQ == c)
                {
                    _stringBuffer.Clear();
                    return EntityDeclarationValue(_stream.Next, c, decl);
                }
            }

            throw Errors.Xml(ErrorCode.DtdEntityInvalid);
        }

        DtdToken EntityDeclarationBeforeValue(Char c, DtdEntityToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);

                if (Specification.DQ == c || Specification.SQ == c)
                    return EntityDeclarationValue(_stream.Next, c, decl);
            }

            throw Errors.Xml(ErrorCode.DtdEntityInvalid);
        }

        DtdToken EntityDeclarationValue(Char c, Char end, DtdEntityToken decl)
        {

            decl.Value = ScanString(c, end);
            return EntityDeclarationAfter(_stream.Next, decl);
        }

        DtdToken EntityDeclarationBeforeSystem(Char c, DtdEntityToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);
                _stringBuffer.Clear();

                if (Specification.DQ == c || Specification.SQ == c)
                    return EntityDeclarationSystem(_stream.Next, c, decl);
            }

            throw Errors.Xml(ErrorCode.DtdEntityInvalid);
        }

        DtdToken EntityDeclarationSystem(Char c, Char quote, DtdEntityToken decl)
        {
            while (c != quote)
            {
                if (!c.IsPubidChar())
                    throw Errors.Xml(ErrorCode.DtdEntityInvalid);

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            decl.PublicIdentifier = _stringBuffer.ToString();
            return EntityDeclarationBeforeValue(_stream.Next, decl);
        }

        DtdToken EntityDeclarationAfter(Char c, DtdEntityToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);

                if (decl.IsExtern && !decl.IsParameter && String.IsNullOrEmpty(decl.ExternNotation) && _stream.ContinuesWith(NDATA))
                {
                    _stream.Advance(4);
                    c = _stream.Next;

                    while (c.IsSpaceCharacter())
                        c = _stream.Next;

                    if (c.IsXmlNameStart())
                    {
                        _stringBuffer.Clear();

                        do
                        {
                            _stringBuffer.Append(c);
                            c = _stream.Next;
                        }
                        while (c.IsXmlName());

                        decl.ExternNotation = _stringBuffer.ToString();
                        return EntityDeclarationAfter(c, decl);
                    }

                    throw Errors.Xml(ErrorCode.DtdEntityInvalid);
                }
            }

            if (c == Specification.EOF)
                throw Errors.Xml(ErrorCode.EOF);
            else if (c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdEntityInvalid);
        }

        #endregion

        #region Attribute Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#attdecls.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdAttributeToken AttributeDeclaration(Char c)
        {
            var decl = new DtdAttributeToken();

            if (DeclarationNameBefore(_stream.Next, decl))
            {
                c = SkipSpaces(c);

                while (true)
                {
                    if (c == Specification.GT)
                        return AttributeDeclarationAfter(c, decl);
                    else if (!c.IsXmlNameStart())
                        break;

                    _stringBuffer.Clear();
                    decl.Attributes.Add(AttributeDeclarationName(c));
                    c = _stream.Current;

                    if(c.IsSpaceCharacter())
                        c = SkipSpaces(c);
                }
            }

            throw Errors.Xml(ErrorCode.DtdAttListInvalid);
        }

        AttributeDeclarationEntry AttributeDeclarationName(Char c)
        {
            var value = new AttributeDeclarationEntry();

            do
            {
                _stringBuffer.Append(c);
                c = _stream.Next;
            }
            while (c.IsXmlName());

            if (!c.IsSpaceCharacter())
                throw Errors.Xml(ErrorCode.DtdAttListInvalid);

            value.Name = _stringBuffer.ToString();
            _stringBuffer.Clear();
            return AttributeDeclarationType(_stream.Next, value);
        }

        AttributeDeclarationEntry AttributeDeclarationType(Char c, AttributeDeclarationEntry value)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.RBO)
            {
                var type = new AttributeEnumeratedType();
                value.Type = type;
                AttributeDeclarationTypeEnumeration(_stream.Next, type);
            }
            else if (c.IsUppercaseAscii())
            {
                var id = String.Empty;

                while (true)
                {
                    if (c.IsSpaceCharacter())
                    {
                        id = _stringBuffer.ToString();
                        _stringBuffer.Clear();
                        break;
                    }
                    else if (c == Specification.GT)
                        throw Errors.Xml(ErrorCode.DtdAttListInvalid);
                    else if (c == Specification.NULL)
                    {
                        RaiseErrorOccurred(ErrorCode.NULL);
                        _stringBuffer.Append(Specification.REPLACEMENT);
                    }
                    else if (c == Specification.EOF)
                        throw Errors.Xml(ErrorCode.EOF);
                    else
                        _stringBuffer.Append(c);

                    c = _stream.Next;
                }

                switch (id)
                {
                    case CDATA:
                        value.Type = new AttributeStringType();
                        break;

                    case ID:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ID };
                        break;

                    case IDREF:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.IDREF };
                        break;

                    case IDREFS:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.IDREFS };
                        break;

                    case ENTITY:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ENTITY };
                        break;

                    case ENTITIES:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.ENTITIES };
                        break;

                    case NMTOKEN:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.NMTOKEN };
                        break;

                    case NMTOKENS:
                        value.Type = new AttributeTokenizedType { Value = AttributeTokenizedType.TokenizedType.NMTOKENS };
                        break;

                    case NOTATION:
                        var type = new AttributeEnumeratedType { IsNotation = true };
                        value.Type = type;

                        while (c.IsSpaceCharacter())
                            c = _stream.Next;

                        if (c != Specification.RBO)
                            throw Errors.Xml(ErrorCode.DtdAttListInvalid);

                        AttributeDeclarationTypeEnumeration(_stream.Next, type);
                        break;

                    default:
                        throw Errors.Xml(ErrorCode.DtdAttListInvalid);
                }
            }

            return AttributeDeclarationValue(_stream.Next, value);
        }

        void AttributeDeclarationTypeEnumeration(Char c, AttributeEnumeratedType parent)
        {
            while (true)
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.EOF)
                    throw Errors.Xml(ErrorCode.EOF);

                if (!c.IsXmlName())
                    throw Errors.Xml(ErrorCode.DtdAttListInvalid);

                do
                {
                    _stringBuffer.Append(c);
                    c = _stream.Next;
                }
                while (c.IsXmlName());

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                parent.Names.Add(_stringBuffer.ToString());
                _stringBuffer.Clear();

                if (c == Specification.RBC)
                    break;
                else if (c == Specification.PIPE)
                    c = _stream.Next;
                else
                    throw Errors.Xml(ErrorCode.DtdAttListInvalid);
            }
        }

        AttributeDeclarationEntry AttributeDeclarationValue(Char c, AttributeDeclarationEntry value)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            var isfixed = false;

            if (c == Specification.NUM)
            {
                do
                {
                    _stringBuffer.Append(c);
                    c = _stream.Next;

                    if (c == Specification.EOF)
                        throw Errors.Xml(ErrorCode.EOF);
                    else if (c == Specification.GT)
                        break;
                }
                while (!c.IsSpaceCharacter());

                var tag = _stringBuffer.ToString();
                _stringBuffer.Clear();

                switch (tag)
                {
                    case REQUIRED:
                        value.Default = new AttributeRequiredValue();
                        return value;
                    case IMPLIED:
                        value.Default = new AttributeImpliedValue();
                        return value;
                    case FIXED:
                        isfixed = true;
                        break;
                }

                while (c.IsSpaceCharacter())
                    c = _stream.Next;
            }

            var defvalue = AttributeDeclarationBeforeDefaultValue(c);
            _stream.Advance();

            value.Default = new AttributeCustomValue
            {
                Value = defvalue,
                IsFixed = isfixed
            };
            return value;
        }

        String AttributeDeclarationBeforeDefaultValue(Char c)
        {
            if (c == Specification.DQ || c == Specification.SQ)
                return ScanString(_stream.Next, c);

            throw Errors.Xml(ErrorCode.DtdAttListInvalid);
        }

        DtdAttributeToken AttributeDeclarationAfter(Char c, DtdAttributeToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdAttListInvalid);
        }

        #endregion

        #region Notation Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#Notations.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdNotationToken NotationDeclaration(Char c)
        {
            if (c.IsSpaceCharacter())
            {
                var decl = new DtdNotationToken();

                if (DeclarationNameBefore(_stream.Next, decl))
                {
                    c = SkipSpaces(c);

                    if (_stream.ContinuesWith(PUBLIC))
                    {
                        _stream.Advance(5);
                        return NotationDeclarationBeforePublic(_stream.Next, decl);
                    }
                    else if (_stream.ContinuesWith(SYSTEM))
                    {
                        _stream.Advance(5);
                        return NotationDeclarationBeforeSystem(_stream.Next, decl);
                    }

                    return NotationDeclarationAfterSystem(c, decl);
                }
            }

            throw Errors.Xml(ErrorCode.DtdNotationInvalid);
        }

        DtdNotationToken NotationDeclarationBeforePublic(Char c, DtdNotationToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.SQ || c == Specification.DQ)
                    return NotationDeclarationPublic(_stream.Next, c, decl);
            }

            throw Errors.Xml(ErrorCode.DtdNotationInvalid);
        }

        DtdNotationToken NotationDeclarationPublic(Char c, Char quote, DtdNotationToken decl)
        {
            _stringBuffer.Clear();

            while (c != quote)
            {
                if (c == Specification.EOF)
                    throw Errors.Xml(ErrorCode.DtdNotationInvalid);
                else if (!c.IsPubidChar())
                    throw Errors.Xml(ErrorCode.XmlInvalidPubId);

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            decl.PublicIdentifier = _stringBuffer.ToString();
            return NotationDeclarationAfterPublic(_stream.Next, decl);
        }

        DtdNotationToken NotationDeclarationAfterPublic(Char c, DtdNotationToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                c = SkipSpaces(c);

                if (c == Specification.SQ || c == Specification.DQ)
                    return NotationDeclarationSystem(_stream.Next, c, decl);
            }

            if (c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdNotationInvalid);
        }

        DtdNotationToken NotationDeclarationBeforeSystem(Char c, DtdNotationToken decl)
        {
            if (c.IsSpaceCharacter())
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.SQ || c == Specification.DQ)
                    return NotationDeclarationSystem(_stream.Next, c, decl);
            }

            throw Errors.Xml(ErrorCode.DtdNotationInvalid);
        }

        DtdNotationToken NotationDeclarationSystem(Char c, Char quote, DtdNotationToken decl)
        {
            _stringBuffer.Clear();

            while (c != quote)
            {
                if (c == Specification.EOF)
                    throw Errors.Xml(ErrorCode.DtdNotationInvalid);

                _stringBuffer.Append(c);
                c = _stream.Next;
            }

            decl.SystemIdentifier = _stringBuffer.ToString();
            return NotationDeclarationAfterSystem(_stream.Next, decl);
        }

        DtdNotationToken NotationDeclarationAfterSystem(Char c, DtdNotationToken decl)
        {
            if (c.IsSpaceCharacter())
                c = SkipSpaces(c);
            
            if (c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdNotationInvalid);
        }

        #endregion

        #region Type Declaration

        /// <summary>
        /// More http://www.w3.org/TR/REC-xml/#elemdecls.
        /// </summary>
        /// <param name="c">The next input character.</param>
        DtdElementToken TypeDeclaration(Char c)
        {
            var decl = new DtdElementToken();

            if (DeclarationNameBefore(c, decl))
            {
                c = SkipSpaces(c);

                if (c == Specification.RBO)
                    return TypeDeclarationBeforeContent(_stream.Next, decl);
                else if (_stream.ContinuesWith(ANY))
                {
                    _stream.Advance(2);
                    decl.Entry = ElementDeclarationEntry.Any;
                    return TypeDeclarationAfterContent(_stream.Next, decl);
                }
                else if (_stream.ContinuesWith(EMPTY))
                {
                    _stream.Advance(4);
                    decl.Entry = ElementDeclarationEntry.Empty;
                    return TypeDeclarationAfterContent(_stream.Next, decl);
                }

                return TypeDeclarationAfterContent(c, decl);
            }

            throw Errors.Xml(ErrorCode.DtdTypeInvalid);
        }

        DtdElementToken TypeDeclarationBeforeContent(Char c, DtdElementToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (_stream.ContinuesWith(PCDATA))
            {
                _stream.Advance(6);
                decl.Entry = TypeDeclarationMixed(_stream.Next);
            }
            else
            {
                decl.Entry = TypeDeclarationChildren(c);
            }

            return TypeDeclarationAfterContent(_stream.Current, decl);
        }

        ElementChildrenDeclarationEntry TypeDeclarationChildren(Char c)
        {
            var entries = new List<ElementQuantifiedDeclarationEntry>();
            var connection = Specification.NULL;

            while (true)
            {
                if (entries.Count > 0)
                {
                    if (c != Specification.PIPE && c != Specification.COMMA)
                        throw Errors.Xml(ErrorCode.DtdTypeContent);

                    if (entries.Count == 1)
                        connection = c;
                    else if (connection != c)
                        throw Errors.Xml(ErrorCode.DtdTypeContent);

                    c = _stream.Next;
                }

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c.IsXmlNameStart())
                {
                    var name = TypeDeclarationName(c);
                    entries.Add(name);
                }
                else if (c == Specification.RBO)
                    entries.Add(TypeDeclarationChildren(_stream.Next));
                else
                    throw Errors.Xml(ErrorCode.DtdTypeContent);

                c = _stream.Current;

                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.RBC)
                    break;
            }

            c = _stream.Next;

            if (entries.Count == 0)
                throw Errors.Xml(ErrorCode.DtdTypeInvalid);

            if (connection == Specification.COMMA)
            {
                var sequence = new ElementSequenceDeclarationEntry();
                sequence.Sequence.AddRange(entries);
                sequence.Quantifier = TypeDeclarationQuantifier(c);
                return sequence;
            }
            else
            {
                var choice = new ElementChoiceDeclarationEntry();
                choice.Choice.AddRange(entries);
                choice.Quantifier = TypeDeclarationQuantifier(c);
                return choice;
            }
        }

        ElementNameDeclarationEntry TypeDeclarationName(Char c)
        {
            _stringBuffer.Clear();
            _stringBuffer.Append(c);

            while ((c = _stream.Next).IsXmlName())
                _stringBuffer.Append(c);

            return new ElementNameDeclarationEntry
            {
                Name = _stringBuffer.ToString(),
                Quantifier = TypeDeclarationQuantifier(c)
            };
        }

        ElementQuantifier TypeDeclarationQuantifier(Char c)
        {
            switch (c)
            {
                case Specification.ASTERISK:
                    _stream.Advance();
                    return ElementQuantifier.ZeroOrMore;

                case Specification.QM:
                    _stream.Advance();
                    return ElementQuantifier.ZeroOrOne;

                case Specification.PLUS:
                    _stream.Advance();
                    return ElementQuantifier.OneOrMore;

                default:
                    return ElementQuantifier.One;
            }
        }

        ElementMixedDeclarationEntry TypeDeclarationMixed(Char c)
        {
            var entry = new ElementMixedDeclarationEntry();

            while (true)
            {
                while (c.IsSpaceCharacter())
                    c = _stream.Next;

                if (c == Specification.RBC)
                {
                    c = _stream.Next;

                    if (c == Specification.ASTERISK)
                    {
                        entry.Quantifier = ElementQuantifier.ZeroOrMore;
                        _stream.Advance();
                        return entry;
                    }

                    if (entry.Names.Count == 0)
                        break;
                }
                else if (c == Specification.PIPE)
                {
                    c = _stream.Next;

                    while (c.IsSpaceCharacter())
                        c = _stream.Next;

                    _stringBuffer.Clear();

                    if (c.IsXmlNameStart())
                    {
                        _stringBuffer.Append(c);

                        while ((c = _stream.Next).IsXmlName())
                            _stringBuffer.Append(c);

                        entry.Names.Add(_stringBuffer.ToString());
                        continue;
                    }
                }
                
                throw Errors.Xml(ErrorCode.DtdTypeContent);
            }

            return entry;
        }

        DtdElementToken TypeDeclarationAfterContent(Char c, DtdElementToken decl)
        {
            while (c.IsSpaceCharacter())
                c = _stream.Next;

            if (c == Specification.GT)
                return decl;

            throw Errors.Xml(ErrorCode.DtdTypeInvalid);
        }

        #endregion

        #region Intermediate Stream

        sealed class IntermediateStream
        {
            #region Members

            Stack<String> _params;
            Stack<Int32> _pos;
            Stack<String> _texts;
            SourceManager _base;
            Int32 _start;
            Int32 _end;

            #endregion

            #region ctor

            public IntermediateStream(SourceManager src)
            {
                _start = src.InsertionPoint - 1;
                _pos = new Stack<Int32>();
                _params = new Stack<String>();
                _texts = new Stack<String>();
                _base = src;
            }

            #endregion

            #region Properties

            /// <summary>
            /// The content (of the original stream).
            /// </summary>
            public String Content
            {
                get { return _base.Copy(_start, _end); }
            }

            /// <summary>
            /// The next character.
            /// </summary>
            public Char Next
            {
                get 
                {
                    if (_texts.Count == 0)
                    {
                        _end = _base.InsertionPoint;
                        return _base.Next;
                    }
                    else
                    {
                        _pos.Push(_pos.Pop() + 1);

                        if (_pos.Peek() == _texts.Peek().Length)
                        {
                            _pos.Pop();
                            _texts.Pop();
                            _params.Pop();
                        }
                    }

                    return Current;
                }
            }

            /// <summary>
            /// The current character.
            /// </summary>
            public Char Current 
            {
                get { return _texts.Count == 0 ? _base.Current : _texts.Peek()[_pos.Peek()]; }
            }

            #endregion

            #region Methods

            /// <summary>
            /// Pushes the the param entity with its text onto the stack.
            /// </summary>
            /// <param name="param">The param entity to use.</param>
            /// <param name="text">The text to insert.</param>
            public void Push(String param, String text)
            {
                if (_params.Contains(param))
                    throw Errors.Xml(ErrorCode.DtdPEReferenceRecursion);

                Advance();

                if (String.IsNullOrEmpty(text))
                    return;

                _params.Push(param);
                _pos.Push(0);
                _texts.Push(text);
            }

            /// <summary>
            /// Advances by one character.
            /// </summary>
            public void Advance()
            {
                if (_texts.Count != 0)
                {
                    _pos.Push(_pos.Pop() + 1);

                    if (_pos.Peek() == _texts.Peek().Length)
                    {
                        _pos.Pop();
                        _texts.Pop();
                        _params.Pop();
                    }
                }
                else
                    _base.Advance();
            }

            /// <summary>
            /// Advances by n characters.
            /// </summary>
            /// <param name="n">The number of characters to skip.</param>
            public void Advance(Int32 n)
            {
                for (int i = 0; i < n; i++)
                    Advance();
            }

            /// <summary>
            /// Checks if the stream continues with the given word.
            /// </summary>
            /// <param name="word">The word to check for.</param>
            /// <returns>True if it continues, otherwise false.</returns>
            public Boolean ContinuesWith(String word)
            {
                if (_texts.Count == 0)
                    return _base.ContinuesWith(word, false);

                var pos = _pos.Peek();
                var text = _texts.Peek();

                if (text.Length - pos < word.Length)
                    return false;

                for (int i = 0; i < word.Length; i++)
                {
                    if (text[i + pos] != word[i])
                        return false;
                }

                return true;
            }

            #endregion
        }

        #endregion

        #region Helper

        String ScanString(Char c, Char end)
        {
            var buffer = Pool.NewStringBuilder();

            while (c != end)
            {
                if (c == Specification.EOF)
                {
                    buffer.ToPool();
                    throw Errors.Xml(ErrorCode.EOF);
                }
                else if (c == Specification.PERCENT)
                {
                    PEReference(_stream.Next, IsExternal);
                    c = _stream.Current;
                    continue;
                }
                else if (c == Specification.LT && _stream.ContinuesWith("<![CDATA["))
                {
                    _src.Advance(8);
                    c = _stream.Next;

                    while (true)
                    {
                        if (c == Specification.EOF)
                            throw Errors.Xml(ErrorCode.EOF);

                        if (c == Specification.SBC && _stream.ContinuesWith("]]>"))
                        {
                            _stream.Advance(2);
                            break;
                        }

                        buffer.Append(c);
                        c = _src.Next;
                    }
                }
                else if (c == Specification.AMPERSAND)
                    buffer.Append(EReference(_stream.Next));
                else
                    buffer.Append(c);

                c = _stream.Next;
            }

            return buffer.ToPool();
        }

        Char SkipSpaces(Char c)
        {
            do
            {
                c = _stream.Next;
            }
            while (c.IsSpaceCharacter());

            return c;
        }

        #endregion
    }
}
