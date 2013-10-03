﻿using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SyMath;

namespace Circuit
{
    /// <summary>
    /// Ideal current source.
    /// </summary>
    [CategoryAttribute("Standard")]
    [DisplayName("Current Source")]
    public class CurrentSource : TwoTerminal
    {
        [Description("Current generated by this current source as a function of time t.")]
        [SchematicPersistent]
        public Quantity i
        {
            get { return new Quantity(Anode.i, Units.A); }
            set
            {
                if (value.Units != Units.A && value.Units != Units.None)
                    throw new ArgumentException("Invalid units");

                Anode.i = value.Value;
                Cathode.i = -value.Value;

                NotifyChanged("i");
            }
        }

        public CurrentSource()
        {
            Name = "I1";
            i = new Quantity(1, Units.A); 
        }

        protected override void DrawSymbol(SymbolLayout Sym)
        {
            int r = 10;
            Sym.AddWire(Anode, new Coord(0, r));
            Sym.AddWire(Cathode, new Coord(0, -r));

            Sym.AddCircle(ShapeType.Black, new Coord(0, 0), r);
            Sym.DrawArrow(ShapeType.Black, new Coord(0, -7), new Coord(0, 7), 0.2f);

            Sym.DrawText(i.ToString(), new CoordD(r * 0.7, r * 0.7), Alignment.Near, Alignment.Near);
            Sym.DrawText(Name, new CoordD(r * 0.7, r * -0.7), Alignment.Near, Alignment.Far); 
        }
    }
}