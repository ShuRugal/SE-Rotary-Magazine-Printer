using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Lifetime;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Profiler;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{
		//Information----------------------------
		//script for controlling a rotary missile magazine/printer
		public string magTag = "[RotoMag]";			//group identifier for a magazine
		//---------------------------------------------
		//Dictionary for storing mags
		public Dictionary<string, magazine> magazines = new Dictionary<string, magazine>();
		//---------------------------------------------
		//funtional varibles used to store printer and system status infortmation
		public bool armed = false; //player input controlled
		public Dictionary<string, string> state = new Dictionary<string, string>(); // determinstic status of each printer
		Dictionary<string, int> thinge = new Dictionary<string, int>();
		Dictionary<string, int> tfuel = new Dictionary<string, int>();
		Dictionary<string, int> tfired = new Dictionary<string, int>();
		//--------------------------------------------
		//set to accept command inputs
		MyCommandLine _commandLine = new MyCommandLine();
		Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

		public class slot	//class to track the status of a missile slot
		{
			public string state = "empty";
			public IMyShipMergeBlock mergeBlock;
			public IMyShipConnector connector;
		}		
		public class magazine //class to define and interact with a magazine and its components
		{
			public IMyMotorStator rotor;
			public IMyProjector projector;
			public IMyGyro gyro;
			//public List<IMyShipMergeBlock> mergeBlocks = new List<IMyShipMergeBlock>(); - deprecacted
			//public List<IMyShipConnector> connectors = new List<IMyShipConnector>(); - deprecated
			public Dictionary<int, slot> slots = new Dictionary<int, slot>();
			public List<IMyShipWelder> welders = new List<IMyShipWelder>();
			public List<IMyTerminalBlock> doors = new List<IMyTerminalBlock>();
			public string state = "ready";
			public string laststate = "init";
			public string status = "idle";
			public double position;
			public double comandedPosition = 1;
			//public int slots; - deprecated
			public int stepAngle;
			public string name;
		}

		public void findMags(Dictionary<string, magazine> maglist) //discover magazines to control
		{

			//get list of all groups on grid
			var Groups = new List<IMyBlockGroup>();
			GridTerminalSystem.GetBlockGroups(Groups);
			//search that list for our torpedo printers
			Echo("Looking for magazines");
			foreach (var group in Groups)
			{
				if (maglist.Keys.Contains(group.Name)) 
				{
					Echo($"Magazine {group.Name} already exists, skipping...");
					continue; 
				}
				else if (group.Name.Contains(magTag))
				{
					string magname = group.Name;
					Echo($"found group {magname}");
					magazines[magname] = new magazine();
					magazines[magname].name = magname;
					List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
					group.GetBlocks(blocks);
					foreach (var block in blocks)
					{
						string type = block.GetType().Name;
						//Echo(type);
						if (type == "MyMotorAdvancedStator")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].rotor = block as IMyMotorStator;
						}
						else if (type == "MyShipMergeBlock")
						{
							//Echo($"found {block.CustomName}");
							var resultString = block.CustomName.Split('[', ']')[1];
							var slotnum = Int32.Parse(resultString);
							if (magazines[magname].slots.ContainsKey(slotnum) )
							{
								magazines[magname].slots[slotnum].mergeBlock = block as IMyShipMergeBlock;
							}
							else
							{
								magazines[magname].slots[slotnum] = new slot();
								magazines[magname].slots[slotnum].mergeBlock = block as IMyShipMergeBlock;
							}

						}
						else if (type == "MyShipConnector")
						{
							//Echo($"found {block.CustomName}");
							var resultString = block.CustomName.Split('[', ']')[1];
							var slotnum = Int32.Parse(resultString);
							if (magazines[magname].slots.ContainsKey(slotnum))
							{
								magazines[magname].slots[slotnum].connector = block as IMyShipConnector;
							}
							else
							{
								magazines[magname].slots[slotnum] = new slot(); 
								magazines[magname].slots[slotnum].connector = block as IMyShipConnector;
							}

						}
						else if (type == "MyShipWelder")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].welders.Add(block as IMyShipWelder);

						}
						else if (type == "MyAirtightSlideDoor")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].doors.Add(block);

						}
						else if (type == "MySpaceProjector")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].projector = block as IMyProjector;

						}
						else if (type == "MyGyro")
						{
							//Echo($"found {block.CustomName}");
							magazines[magname].gyro = block as IMyGyro;

						}
						//else Echo($"ignoring block \"{block.CustomName}\" of unknown type \"{block.GetType().Name}\"");
					}

				}
			}
			foreach (var mag in magazines)
			{
				var val = mag.Value;
				var key = mag.Key;
				if (
					(val.doors.Count == 0) |
					(val.welders.Count == 0) |
					(val.rotor == null) |
					(val.projector == null) |
					(val.gyro == null)
					)
				{
					Echo($"\"{key}\" is missing parts!");
				}
				else Echo($"\"{key}\" is complete.");
				Echo($"found {val.doors.Count} doors.");
				Echo($"found {val.welders.Count} welders.");
				Echo($"found rotor \"{val.rotor.CustomName}\"");
				Echo($"found projector \"{val.projector.CustomName}\"");
				Echo($"found gyro \"{val.gyro.CustomName}\"");
			}

		}

		public void bootMags() //perform initialization of mags - set their parameters and rotate them to visually confirm they work
		{
			foreach (var entry in magazines)
			{
				magazine mag = entry.Value;
				string name = entry.Key;
				magazines[name].stepAngle = 360 / magazines[name].slots.Count;
				magazines[name].position = (mag.rotor.Angle / 2 * Math.PI * magazines[name].slots.Count +1);
				mag.status = "Bootup Sequence";
				Single ll = -361;
				Single ul = 361;
				Single v = 2;
				mag.rotor.SetValue("LowerLimit", ll);
				mag.rotor.SetValue("UpperLimit", ul);
				mag.rotor.SetValue("Velocity", v);
			}
			// allow 100 ticks for boot cycle to physically move discovered rotors - this is to allow players to visually validate the assembly works
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		public void sitrep() //provide a status report of all known magazines to progblock console and text panel
		{
			string report = "";
			var block = Me;
			var surface = block is IMyTextSurface
				? (IMyTextSurface)block
				: ((IMyTextSurfaceProvider)block).GetSurface(0);
			surface.ContentType = ContentType.TEXT_AND_IMAGE;
			surface.WriteText("");
			foreach (var mag in magazines.Values) { 
				string name = mag.name;
				report += $"Magazine {name}\n";
				report += $"State: {mag.state}\n";
				report += $"Status: {mag.status}\n";
				report += $"Commanded Position: {mag.comandedPosition}\n";
				report += $"Actual Position: {mag.position}\n";
				foreach (var slot in mag.slots.Values)
				{
					report += $"{slot.mergeBlock.CustomName} Connected: {slot.mergeBlock.IsConnected}\n";
				}/*
				report += "Printable Parts\n";
					foreach (var entry in mag.readypartslist)
				{
					report += $"{entry.ToString()}\n";
				}
				report += "target Parts\n";
				foreach (var entry in mag.targetparts)
				{
					report += $"{entry.ToString()}\n";
				}*/
				report += "\n";
			}
			surface.WriteText(report);
			Echo(report);
		}

		public void weldersEnable (magazine mag, bool onoff)
		{
			foreach (var welder in mag.welders)
			{
				welder.Enabled = onoff;
			}
		}

		public void monitor() //monitor the status of the magazine to see if it matches the commanded status and to evalute if actions need to be performed
		{ 
		foreach (var mag in magazines.Values)
			{
				magazines[mag.name].position = (mag.rotor.Angle / (2 * Math.PI) * magazines[mag.name].slots.Count + 1);
				mag.position = Math.Round(magazines[mag.name].position, 2);
				mag.comandedPosition = Math.Round(magazines[mag.name].comandedPosition, 2);
				if (mag.state == "empty" | mag.state == "printing")
				{
					printMissilesFull(mag);
				}
				if (mag.comandedPosition != mag.position)
				{
					rotateMag(mag.name, mag.comandedPosition);
				}
				else if (mag.position == mag.comandedPosition)
				{
					magazines[mag.name].rotor.TargetVelocityRPM = 0;
					mag.rotor.UpperLimitRad = mag.rotor.Angle;
					mag.rotor.LowerLimitRad = mag.rotor.Angle;
					magazines[mag.name].status = $"Idle in position {mag.position}";
					mag.gyro.GyroOverride = false;
				}
				if (mag.state == "ready")
				{
					bool empty = false;
					foreach (var slot in mag.slots.Values)
					{
						if (slot.mergeBlock.IsConnected) empty = false;
						else if (slot.connector.IsConnected) empty = false;
					}
					if (empty == true) magazines[mag.name].state = "empty";
				}

			}
		}

		public void printMissilesFull(magazine mag) //function for controlling the printing of new missiles
		{
			if (mag.state == "empty")
			{
				magazines[mag.name].state = "printing"; //update state
				foreach (var slot in mag.slots.Values) //turn off all merge blocks
				{
					slot.mergeBlock.Enabled = false;
				}
				magazines[mag.name].comandedPosition = 1; //send the mag to position 1 to begin the print cycle
				mag.slots[(int)mag.comandedPosition].mergeBlock.Enabled = true; //turn on the merge block for this slot
			}
			else if (mag.state == "printing" && mag.position == mag.comandedPosition)
			{

				bool slotfinished = (mag.projector.BuildableBlocksCount == 0);
				magazines[mag.name].status = $"printing slot{mag.comandedPosition}";
				
				if (slotfinished == false)
				{
					weldersEnable(mag, true); //turn the welders on
					mag.slots[(int)mag.comandedPosition].state = "Printing";
				}
				else if (slotfinished == true)
				{
					magazines[mag.name].comandedPosition += 1;
					mag.slots[(int)mag.comandedPosition].mergeBlock.Enabled = true; //turn on the merge block for this slot
					mag.slots[(int)mag.comandedPosition].state = "Ready";
				}
				if (mag.projector.RemainingBlocks == 0)
				{
					weldersEnable(mag, false);//turn the welders off
					magazines[mag.name].state = "ready";
				}
			}
		
		}

		public void parsecommands(string argument) //handle user input in the run field of the progblock
		{
			if (_commandLine.TryParse(argument))
			{
				Action commandAction;

				// Retrieve the first argument. Switches are ignored.
				string command = _commandLine.Argument(0);

				// Now we must validate that the first argument is actually specified, 
				// then attempt to find the matching command delegate.
				if (command == null)
				{
					Echo("No command specified");
				}
				else if (_commands.TryGetValue(command, out commandAction))
				{
					// We have found a command. Invoke it.
					commandAction();
				}
				else
				{
					Echo($"Unknown command {command}");
				}
			}

		}

		public void step() //increment the commanded position of all magazines by 1, this will cause the mags to advance on next program loop  this is mainly a troubleshooting measure
		{
			foreach (var mag in magazines.Values) {
				magazines[mag.name].comandedPosition += 1;
			}
		
		}

		

		public void rotateMag(string magname, double newpos) //monitor function will call this if it discovers a misalignment of greater than 1% of 1 step.  this function can also be used directly to command a new mag position
		{
			var mag = magazines[magname];
			mag.gyro.GyroOverride = true; //set our gyro to hold current heading to counter torque of rotating the magazine
			magazines[magname].comandedPosition = newpos; //if we called this manually, we need to store the new commanded position so the monitor function does not move the mag back
			float gotoangle = (float)(mag.stepAngle * (mag.comandedPosition - 1));
			if ( mag.comandedPosition > mag.slots.Count) //check if we are commanding an invalid position.  if so, go to 1
			{
				magazines[mag.name].comandedPosition = 1;
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				mag.rotor.TargetVelocityRPM = 15;
				mag.rotor.UpperLimitDeg = gotoangle;
			}
			else if (mag.position > mag.comandedPosition)
			{
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				
				mag.rotor.TargetVelocityRPM = -15;
				mag.rotor.LowerLimitDeg = gotoangle;
			}
			else if (mag.position < mag.comandedPosition)
			{
				magazines[mag.name].status = $"Moving to position {mag.comandedPosition}";
				mag.rotor.TargetVelocityRPM = 15;
				mag.rotor.UpperLimitDeg = gotoangle;
			}
			else if (mag.position == mag.comandedPosition)
			{
				magazines[mag.name].rotor.TargetVelocityRPM = 0;
				mag.rotor.UpperLimitDeg = gotoangle;
				mag.rotor.LowerLimitDeg = gotoangle;
				magazines[mag.name].status = $"Idle in position {mag.position}";
				mag.gyro.GyroOverride = false;
			}

		}

		public Dictionary<string, int> readprojector(IMyProjector projector) //get the status of the projector
		{
			Dictionary<string, int> partlist = new Dictionary<string, int>();
			foreach (var entry in projector.RemainingBlocksPerType)
				{
					partlist.Add(entry.Key.ToString(), entry.Value);
				}
			return partlist;
		}
		
		//Startup sequence
		public Program()
		{

			findMags(magazines);
			bootMags();
			_commands["step"] = step;
			foreach (var mag in magazines.Values)
			{
				weldersEnable(mag, false);
			}
		}




		public void Save()
		{
			// Called when the program needs to save its state. Use
			// this method to save your state to the Storage field
			// or some other means. 
			// 
			// This method is optional and can be removed if not
			// needed.
		}

		public void Main(string argument, UpdateType updateSource)
		{
			// Configure this program to run the Main method every 10 update ticks
			Runtime.UpdateFrequency = UpdateFrequency.Update10;
			monitor();
			sitrep();
			parsecommands(argument);
		}
	}
}
