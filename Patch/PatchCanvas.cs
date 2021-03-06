﻿/* ----------------------------------------------------------------------------
Kohoutech Patch Library
Copyright (C) 1995-2020  George E Greaney

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
----------------------------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Kohoutech.Patch
{
    public class PatchCanvas : Control
    {
        public IPatchModel patchModel;         //the canvas' backing model
        public PatchPalette palette;

        List<PatchBox> boxList;             //the boxes on the canvas
        List<PatchWire> wireList;           //the wires on the canvas

        List<Object> zList;                 //z-order for painting boxes & wires

        PatchBox selectedBox;           //currently selected box
        PatchWire selectedWire;         //currently select wire

        Point newBoxOrg;                //point on canvas where first new box is placed
        Point newBoxOfs;                //offset of next new box is placed
        Point newBoxPos;                //point where next new box is placed

        bool dragging;
        Point dragOrg;
        Point dragOfs;

        bool connecting;
        Point connectWireStart;
        Point connectWireEnd;
        PatchPanel sourcePanel;
        PatchPanel targetPanel;

        bool tracking;
        PatchPanel trackingPanel;

        //cons
        public PatchCanvas(IPatchModel _patchModel)
        {
            patchModel = _patchModel;

            palette = new PatchPalette(this);
            palette.Location = new Point(this.ClientRectangle.Left, this.ClientRectangle.Top);
            palette.Size = new Size(palette.Width, this.ClientRectangle.Height);
            this.Controls.Add(palette);

            this.BackColor = Color.White;
            this.DoubleBuffered = true;

            boxList = new List<PatchBox>();
            wireList = new List<PatchWire>();
            zList = new List<object>();

            newBoxOrg = new Point(palette.Width + 50, 50);
            newBoxOfs = new Point(20, 20);
            newBoxPos = new Point(newBoxOrg.X, newBoxOrg.Y);

            //init canvas state
            selectedBox = null;             //nothing selected yet
            selectedWire = null;
            dragging = false;               //not dragging
            tracking = false;
            trackingPanel = null;
            connecting = false;             //not connecting
            sourcePanel = null;
            targetPanel = null;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (palette != null)
            {
                palette.Size = new Size(palette.Width, this.ClientRectangle.Height);
            }
        }

        public void setCanvasColor(Color color)
        {
            this.BackColor = color;
            this.Invalidate();
        }

        //- palette management ----------------------------------------------------------

        internal void openPalette(bool isOpen)
        {
            if (isOpen)
            {
                palette.Location = new Point(this.ClientRectangle.Left, this.ClientRectangle.Top);
            }
            else
            {
                palette.Location = new Point(this.ClientRectangle.Left - palette.Width + palette.buttonWidth, this.ClientRectangle.Top);
            }
            this.Invalidate();          //redraw palette border
        }

        public void setPaletteColor(Color color)
        {
            palette.BackColor = color;
            palette.Invalidate();
        }

        public void addPaletteGroup(string groupName)
        {
            palette.addGroup(groupName);
        }

        public void addPaletteItem(string groupName, string itemName, string modelName)
        {
            palette.addItem(groupName, itemName, modelName);
        }

        public void enablePaletteItem(string itemName)
        {
            palette.enableItem(itemName, true);
        }

        public void disablePaletteItem(string itemName)
        {
            palette.enableItem(itemName, false);
        }

        //dbl clicking on palette item gets a new model box from the model from the model name associated with the palette item
        //a new patch box is created from this model box & added to the canvas
        internal void handlePaletteItemDoubleClick(String modelName)
        {
            IPatchBox newBox = patchModel.getPatchBox(modelName);
            addPatchBox(newBox);
        }

        //- patch management ----------------------------------------------------------

        //removes are the boxes & wires from the canvas which will
        //in turn remove the matching boxes & wires from the model
        public void clearPatch()
        {
            //connections - remove the wires first
            List<PatchWire> delWireList = new List<PatchWire>(wireList);
            foreach (PatchWire wire in delWireList)
            {
                removePatchWire(wire);
            }

            //then remove all the boxes
            List<PatchBox> delboxList = new List<PatchBox>(boxList);
            foreach (PatchBox box in delboxList)
            {
                removePatchBox(box);
            }

            newBoxPos = new Point(newBoxOrg.X, newBoxOrg.Y);
        }

        //- box methods ---------------------------------------------------------------

        //add a patch box at default location on canvas
        public void addPatchBox(IPatchBox boxModel)
        {
            addPatchBox(boxModel, newBoxPos.X, newBoxPos.Y);
            newBoxPos.Offset(newBoxOfs);
            if (!this.ClientRectangle.Contains(newBoxOrg))
            {
                newBoxPos = new Point(newBoxOrg.X, newBoxOrg.Y);      //if we've gone outside the canvas, reset to original new box pos
            }
        }

        public void addPatchBox(IPatchBox boxModel, int xpos, int ypos)
        {
            PatchBox box = new PatchBox(boxModel);
            box.canvas = this;
            box.setPos(new Point(xpos, ypos));
            boxList.Add(box);
            zList.Add(box);
            Invalidate();
        }

        public void removePatchBox(PatchBox box)
        {
            List<PatchWire> wires = box.getWireList();
            foreach (PatchWire wire in wires)
            {
                removePatchWire(wire);                  //remove all connections first
            }
            box.remove();                               //tell box to remove its model from patch
            boxList.Remove(box);                        //and remove box from canvas            
            zList.Remove(box);
            Invalidate();
        }

        public void deselectCurrentSelection()
        {
            //deselect current selection, if there is one
            if (selectedBox != null)
            {
                selectedBox.setSelected(false);
                selectedBox = null;
            }
            if (selectedWire != null)
            {
                selectedWire.Selected = false;
                selectedWire = null;
            }
        }

        public void selectPatchBox(PatchBox box)
        {
            deselectCurrentSelection();
            List<PatchWire> wires = box.getWireList();      //first make all box's wires top of z-order
            foreach (PatchWire wire in wires)
            {
                zList.Remove(wire);
                zList.Add(wire);
            }
            zList.Remove(box);                  //remove the box from its place in z-order
            zList.Add(box);                     //and add to end of list, making it topmost
            selectedBox = box;                  //mark box as selected for future operations
            selectedBox.setSelected(true);      //and let it know it is
            Invalidate();
        }

        public List<PatchBox> getBoxList()
        {
            return boxList;
        }

        //- wire methods ---------------------------------------------------------------

        public void addPatchWire(PatchWire wire)
        {
            wire.canvas = this;
            wireList.Add(wire);                                      //add to canvas
            zList.Add(wire);
            Invalidate();
        }

        //patch wire will be connected to source jack, but may or may not be connected to dest jack
        public void removePatchWire(PatchWire wire)
        {
            wire.disconnect();                          //and disconnect wire from source & dest jacks
            wireList.Remove(wire);
            zList.Remove(wire);
            Invalidate();
        }

        //selecting a wire does NOT change its z-order pos
        public void selectPatchWire(PatchWire wire)
        {
            deselectCurrentSelection();
            selectedWire = wire;                 //mark wire as selected for future operations
            selectedWire.Selected = true;        //and let it know it is
            Invalidate();
        }

        public List<PatchWire> getWireList()
        {
            return wireList;
        }

        //- mouse handling ------------------------------------------------------------

        //dragging & connecting are handled while mouse button is pressed and end when it it let up
        //all other ops are handled with mouse clicks
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Focus();
            bool handled = false;

            //go in reverse z-order so we check topmost first
            for (int i = zList.Count - 1; i >= 0; i--)
            {
                object obj = zList[i];
                if (obj is PatchWire)
                {
                    PatchWire wire = (PatchWire)obj;
                    if (wire.hitTest(e.Location))           //clicked on a wire
                    {
                        selectPatchWire(wire);
                        handled = true;
                        break;
                    }
                }

                if (!handled && obj is PatchBox)
                {
                    PatchBox box = (PatchBox)obj;
                    if (box.hitTest(e.Location))            //clicked on a box
                    {
                        selectPatchBox(box);

                        //we clicked somewhere inside a patchbox (and selected it), check if dragging or connecting                    
                        if (selectedBox.dragTest(e.Location))           //if we clicked on title panel
                        {
                            startDrag(e.Location);
                        }
                        else
                        {
                            PatchPanel panel = selectedBox.panelHitTest(e.Location);
                            if (panel != null)
                            {
                                if (panel.canConnectOut())                 //if we clicked on out jack panel
                                {
                                    startConnection(panel, e.Location);
                                }
                                else if (panel.canTrackMouse())             //if we clicked on a panel that tracks mouse input
                                {
                                    tracking = true;
                                    trackingPanel = panel;
                                    trackingPanel.onMouseDown(e.Location);
                                    Invalidate();
                                }
                            }
                        }
                        handled = true;
                        break;
                    }
                }
            }

            //we clicked on a blank area of the canvas - deselect current selection if there is one
            if (!handled)
            {
                deselectCurrentSelection();
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                drag(e.Location);
            }

            if (connecting)
            {
                moveConnection(e.Location);
            }

            if (tracking)
            {
                trackingPanel.onMouseMove(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging)
            {
                endDrag(e.Location);
            }

            if (connecting)
            {
                finishConnection(e.Location);
            }

            if (tracking)
            {
                trackingPanel.onMouseUp(e.Location);
                trackingPanel = null;
                tracking = false;
                Invalidate();
            }
        }

        //this will ONLY be called if the panel we clicked on is NOT tracking the mouse
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (selectedBox != null && !dragging && !tracking)
            {
                PatchPanel panel = selectedBox.panelHitTest(e.Location);
                if (panel != null)
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        panel.onRightClick(e.Location);
                    }
                    else
                    {
                        panel.onClick(e.Location);
                    }
                }
            }
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (selectedWire != null)
            {
                selectedWire.onDoubleClick(e.Location);
            }
            else if (selectedBox != null)
            {
                if (dragging)       //if we're dragging, then we've double clicked in the title bar
                {
                    selectedBox.onTitleDoubleClick();
                }
                else
                {
                    PatchPanel panel = selectedBox.panelHitTest(e.Location);
                    if (panel != null)
                    {
                        panel.onDoubleClick(e.Location);
                    }
                }
            }
            Invalidate();
        }

        //- keyboard handling ---------------------------------------------------------

        //delete key removes currently selected box & any connections to other boxes from canvas
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (selectedBox != null)
                {
                    removePatchBox(selectedBox);
                    selectedBox = null;
                }

                if (selectedWire != null)
                {
                    removePatchWire(selectedWire);
                    selectedWire = null;
                }
            }
        }

        //- dragging ------------------------------------------------------------------

        //track diff between pos when mouse button was pressed and where it is now, and move box by the same offset
        private void startDrag(Point p)
        {
            dragging = true;
            dragOrg = selectedBox.getPos();
            dragOfs = p;
        }

        private void drag(Point p)
        {
            int newX = p.X - dragOfs.X;
            int newY = p.Y - dragOfs.Y;
            selectedBox.setPos(new Point(dragOrg.X + newX, dragOrg.Y + newY));
            Invalidate();
        }

        //we've finished a drag, let the model know the layout has changed
        private void endDrag(Point p)
        {
            patchModel.layoutHasChanged();
            dragging = false;
        }

        //- connecting ------------------------------------------------------------------

        //for now, connections start from selected box's output jack
        //if it doesn't have output jack, it would fail jack hit test & not get here
        private void startConnection(PatchPanel panel, Point p)
        {
            connecting = true;
            sourcePanel = panel;
            connectWireStart = sourcePanel.ConnectionPoint();
            connectWireEnd = p;
            targetPanel = null;
            Invalidate();
        }

        private void moveConnection(Point p)
        {
            connectWireEnd = p;

            //check if currently over a possible target box
            bool handled = false;
            for (int i = boxList.Count - 1; i >= 0; i--)
            {
                PatchBox box = boxList[i];
                if (box.hitTest(p))
                {
                    if (box != selectedBox)         //check selected box in case another box is under it, but don't connect to itself
                    {
                        PatchPanel panel = box.panelHitTest(p);
                        if (panel != null && !panel.isConnected() && panel.canConnectIn(sourcePanel))
                        {
                            if (targetPanel != null)
                            {
                                targetPanel.patchbox.setTargeted(false);    //deselect current target, if there is one
                            }
                            targetPanel = panel;                            //mark panel as current target, if we drop connection on it
                            targetPanel.patchbox.setTargeted(true);         //and let the panel's box know it
                            handled = true;
                        }
                    }
                    break;
                }
            }

            //if we aren't currently over any targets, unset prev target, if one
            if ((!handled) && (targetPanel != null))
            {
                targetPanel.patchbox.setTargeted(false);
                targetPanel = null;
            }

            Invalidate();
        }

        private void finishConnection(Point p)
        {
            if (targetPanel != null)                              //drop connection on target box we are currently over
            {
                targetPanel.patchbox.setTargeted(false);

                //create new wire & connect it to source & dest panels
                IPatchWire wireModel = patchModel.getPatchWire(sourcePanel.model, targetPanel.model);
                PatchWire newWire = new PatchWire(sourcePanel, targetPanel, wireModel);
                addPatchWire(newWire);
            }

            targetPanel = null;
            sourcePanel = null;
            connecting = false;
        }

        //- painting ------------------------------------------------------------------

        //if the backing model changes due to an internal action and the canvas
        //needs to be redrawn to reflect this
        public void redraw()
        {
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            //palette border
            g.DrawLine(Pens.Black, palette.Right, palette.Top, palette.Right, palette.Bottom);

            //z-order is front to back - the last one in list is topmost
            foreach (Object obj in zList)
            {
                if (obj is PatchBox)
                {
                    ((PatchBox)obj).paint(g);
                }
                if (obj is PatchWire)
                {
                    ((PatchWire)obj).paint(g);
                }
            }

            //temporary connecting line
            if (connecting)
            {
                g.DrawLine(Pens.Red, connectWireStart, connectWireEnd);
            }
        }
    }

    //- model interface -------------------------------------------------------

    public interface IPatchModel
    {
        //allow the model to invalidate the canvas when the canvas should reflect changes in the model
        //        void setCanvas(PatchCanvas canvas);

        //allow the backing model to create a patch unit using the model name stored in palette item's tag field
        IPatchBox getPatchBox(String modelName);

        //allow the backing model to create a patch wire model and connect it to source & dest panels in the model
        IPatchWire getPatchWire(IPatchPanel source, IPatchPanel dest);

        //notify the model that the box/wire layout has changed if it needs to act on this
        void layoutHasChanged();
    }
}

//Console.WriteLine("there's no sun in the shadow of the Wizard");
