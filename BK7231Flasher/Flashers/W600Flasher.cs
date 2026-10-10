using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace BK7231Flasher
{
	public class W600Flasher : BaseFlasher
	{
		private const int FlashSize = 0x100000;
		private const int HelperSectorOffset = 0x10000;
		private const int HelperSectorSize = 0x1000;
		private const int FlsHeaderSize = 56;
		private const int HelperTransferPayloadSize = HelperSectorSize - FlsHeaderSize;
		private const uint FlashBase = 0x08000000;
		private const uint HelperVectorAddress = 0x08010100;
		private const uint HelperResetAddress = 0x08010141;
		private const uint HelperPayloadAddress = 0x08010180;
		private const uint HelperRamAddress = 0x20030000;
		private const uint Uart0BaudRateControl = 0x40010810;
		private const uint HelperApbClock = 40000000;
		private const int HelperMaxReadLength = 1024;
		private const int ProtocolRequestSize = 16;
		private const int ProtocolResponseSize = 20;
		private const byte ProtocolVersion = 1;
		private const byte ProtocolInfo = 0;
		private const byte ProtocolRead = 1;

		byte[] flashID;
		MemoryStream ms;

		public W600Flasher(CancellationToken ct) : base(ct)
		{
		}

		private bool doGenericSetup()
		{
			addLog("Now is: " + DateTime.Now.ToLongDateString() + " " + DateTime.Now.ToLongTimeString() + "." + Environment.NewLine);
			addLog("Flasher mode: " + chipType + Environment.NewLine);
			addLog("Going to open port: " + serialName + "." + Environment.NewLine);
			try
			{
				serial = new SerialPort(serialName, 115200);
				serial.Open();
				serial.DiscardInBuffer();
				serial.DiscardOutBuffer();
				serial.ReadTimeout = 2000;
				xm = new XMODEM(serial, XMODEM.Variants.XModem1K, 0xFF)
				{
					SendInactivityTimeoutMillisec = 5000,
					MaxSenderRetries = 5
				};
			}
			catch(Exception ex)
			{
				addLog("Port setup failed with " + ex.Message + "!" + Environment.NewLine);
				return false;
			}
			addLog("Port ready!" + Environment.NewLine);
			return true;
		}

		private byte[] ReadFlashId()
		{
			var res = ExecuteCommand(0x3C, null, 1, 10, isErrorExpected: true);
			if(res != null && res[0] == 'F' && res[1] == 'I' && res[2] == 'D')
			{
				flashID = new byte[] { Convert.ToByte($"{(char)res[4]}{(char)res[5]}", 16) };
				addLogLine($"Flash ID: 0x{flashID[0]:X}");
				return flashID;
			}
			addLogLine("Getting flash id failed, assuming device is in secboot mode.");
			addLogLine("Erasing secboot, will resync...");
			ExecuteCommand(0x3F, null, 1, 2);
			if(!Sync())
				return null;
			return ReadFlashId();
		}

		private bool Sync()
		{
			serial.DiscardInBuffer();
			var count = 0;
			try
			{
				int attempts = 0;
				while(attempts++ < 1000)
				{
					byte sync = 0;
					try { sync = (byte)serial.ReadByte(); } catch { }
					if(sync == 'C')
					{
						count++;
					}
					else
					{
						if(sync == 'P')
							continue;
						for(int i = 0; i < 250; i++)
						{
							serial.Write(new byte[] { 0x1B }, 0, 1);
							Thread.Sleep(1);
						}
						addLogLine($"Sync attempt {attempts}/1000 failed...");
						serial.DiscardInBuffer();
						count = 0;
					}
					if(count > 3)
					{
						addLogLine("Sync success!");
						return true;
					}
				}
			}
			catch(Exception ex)
			{
				addErrorLine(ex.Message);
			}
			return false;
		}

		private byte[] ReadFlashIdWithoutErase()
		{
			var res = ExecuteCommand(0x3C, null, 1, 10, isErrorExpected: true);
			if(res == null || res.Length < 6 || res[0] != 'F' || res[1] != 'I' ||
				res[2] != 'D' || res[3] != ':')
			{
				addErrorLine("W600 ROM flash-ID probe failed. The read operation will not send the secboot erase command.");
				return null;
			}
			try
			{
				flashID = new byte[] { Convert.ToByte($"{(char)res[4]}{(char)res[5]}", 16) };
			}
			catch(Exception)
			{
				addErrorLine("W600 ROM returned an invalid flash-ID response.");
				return null;
			}
			addLogLine($"Flash ID: 0x{flashID[0]:X}");
			return flashID;
		}

		private bool InitialiseTarget()
		{
			return Sync() && ReadFlashId() != null;
		}

		private byte[] ExecuteCommand(int type, byte[] parms = null,
			float timeout = 0.1f, int expectedReplyLen = 0, int br = 115200, bool isErrorExpected = false)
		{
			parms = parms ?? new byte[0];
			var cmd = new List<byte>()
			{
				(byte)(type & 0xFF),
				(byte)((type >> 8) & 0xFF),
				(byte)((type >> 16) & 0xFF),
				(byte)((type >> 24) & 0xFF)
			};
			cmd.AddRange(parms);
			var raw = new List<byte>()
			{
				0x21,
				(byte)(cmd.Count + 2 & 0xFF),
				(byte)((cmd.Count + 2 >> 8) & 0xFF)
			};
			var crc = CRC16.Compute(CRC16Type.CCITT_FALSE, cmd.ToArray());
			raw.Add((byte)(crc & 0xFF));
			raw.Add((byte)((crc >> 8) & 0xFF));
			raw.AddRange(cmd);

			serial.DiscardInBuffer();
			serial.Write(raw.ToArray(), 0, raw.Count);
			if(type == 0x31)
			{
				Thread.Sleep(10);
				serial.BaudRate = br;
			}
			int timeoutMs = (int)(timeout * 1000);
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while(sw.ElapsedMilliseconds < timeoutMs)
			{
				if(serial.BytesToRead >= expectedReplyLen)
					break;
			}
			if(serial.BytesToRead == 0)
			{
				if(!isErrorExpected) addErrorLine("Command response is empty!");
				return null;
			}
			var bytes = new byte[serial.BytesToRead];
			serial.Read(bytes, 0, bytes.Length);
			if(bytes.Length < expectedReplyLen)
			{
				if(!isErrorExpected) addErrorLine($"Command reply length {bytes.Length} < expected {expectedReplyLen}");
				return null;
			}
			var ret = new byte[expectedReplyLen];
			Array.Copy(bytes, ret, expectedReplyLen);
			return ret;
		}

		private bool SetBaud(int baud, bool noResync = false)
		{
			if(serial.BaudRate == baud)
				return true;
			addLogLine($"Changing baud to {baud}{(!noResync ? ", will resync..." : string.Empty)}");
			byte[] msg = BitConverter.GetBytes(baud);
			ExecuteCommand(0x31, msg, 1, 1, baud, noResync);
			return noResync || Sync();
		}

		private bool EraseAndWait(string label, byte[] parms, int timeoutSeconds)
		{
			addLogLine(label);
			var response = ExecuteCommand(0x32, parms, timeoutSeconds, 4);
			return response != null && response.Length >= 4 &&
				response[0] == 'C' && response[1] == 'C' && response[2] == 'C' && response[3] == 'C';
		}

		public override void doRead(int startSector = 0x000, int sectors = 10, bool fullRead = false)
		{
			ms = null;
			if(!TryCalculateHelperBaudRegister(baudrate, out uint helperBaudRegister))
			{
				addErrorLine($"W600 UART reader does not support the selected {baudrate} baud rate. " +
					"Select one of: 115200, 230400, 460800, 921600, 1500000, or 2000000.");
				return;
			}
			int offset = fullRead ? 0 : startSector;
			int length;
			try
			{
				length = fullRead ? FlashSize : checked(sectors * BK7231Flasher.SECTOR_SIZE);
			}
			catch(OverflowException)
			{
				addErrorLine("W600 read length is out of range.");
				return;
			}
			if(offset < 0 || length <= 0 || offset > FlashSize - length)
			{
				addErrorLine("W600 read range must stay within the 1 MiB flash.");
				return;
			}
			if(!doGenericSetup())
				return;

			try
			{
				xm.PacketSent += Xm_PacketSent;
				addLogLine("W600 UART backup installs a read-only helper in sector 0x10000-0x10FFF.");
				addLogLine("Hold PA0 low, reset the W600 into ROM mode, and keep PA0 low while the helper is written.");
				if(!Sync() || ReadFlashIdWithoutErase() == null)
					return;

				byte[] helperPayload = BuildHelperPayload(helperBaudRegister);
				byte[] fls = GenerateW600PseudoFLSFromData(helperPayload,
					(int)(FlashBase + (uint)HelperSectorOffset));
				if(fls.Length != HelperSectorSize)
					throw new InvalidDataException("The W600 helper FLS transfer must be exactly one 4 KiB XMODEM span.");
				if(!SetBaud(baudrate))
				{
					logger.setState("Helper install failed", Color.Red);
					addErrorLine($"Failed to establish the W600 ROM link at {baudrate} baud.");
					return;
				}
				addLogLine($"Installing the one-sector W600 UART read helper for {baudrate} baud...");
				if(xm.Send(fls, (uint)HelperSectorOffset) != fls.Length)
				{
					logger.setState("Helper install failed", Color.Red);
					logger.setLogProgress("W600 UART read-helper write failed." + Environment.NewLine, Color.Red);
					return;
				}

				logger.setLogProgress("W600 UART read-helper sector written." + Environment.NewLine, Color.Green);
				serial.BaudRate = baudrate;
				serial.DiscardInBuffer();
				logger.setState("Reset W600 to start reader", Color.DarkOrange);
				addLogLine("Helper installed. Release PA0, then press and release RESET. Waiting up to 90 seconds for the reader...");
				if(!WaitForHelper(90000, out int helperChunk))
				{
					if(isCancelled)
					{
						logger.setLogProgress("Waiting for W600 UART reader cancelled." + Environment.NewLine, Color.Orange);
						return;
					}
					logger.setLogProgress("Waiting for W600 UART reader timed out." + Environment.NewLine, Color.Red);
					addErrorLine("Timed out waiting for the W600 UART reader after helper installation.");
					return;
				}

				logger.setState("Reading W600 flash", Color.Green);
				int chunkSize = Math.Min(helperChunk, HelperMaxReadLength);
				byte[] result = ReadHelperRange(FlashBase + (uint)offset, length, chunkSize);
				if(result == null)
				{
					if(!isCancelled)
						logger.setState("Read error", Color.Red);
					return;
				}
				ms = new MemoryStream(result);
				logger.setProgress(1, 1);
				logger.setState("Reading done", Color.DarkGreen);
				addLogLine($"Done W600 flash read: 0x{offset:X6}-0x{offset + length - 1:X6} ({length} bytes).");
			}
			catch(Exception ex)
			{
				if(isCancelled)
					return;
				logger.setState("Read error", Color.Red);
				addErrorLine("W600 read failed: " + ex.Message);
			}
			finally
			{
				xm.PacketSent -= Xm_PacketSent;
			}
		}

		public override byte[] getReadResult()
		{
			return ms?.ToArray();
		}

		public override bool saveReadResult(int startOffset)
		{
			if(ms == null)
				return false;
			string fileName = MiscUtils.formatDateNowFileName("readResult_" + chipType, backupName, "bin");
			string fullPath = "backups/" + fileName;
			byte[] data = ms.ToArray();
			File.WriteAllBytes(fullPath, data);
			addLogLine("Wrote " + data.Length + " bytes to " + fileName);
			logger.onReadResultQIOSaved(data, "", fullPath);
			return true;
		}

		public override void doWrite(int startSector, byte[] data)
		{
			return;
		}

		public override bool doErase(int startSector = 0x000, int sectors = 10, bool bAll = false)
		{
			if(!bAll)
			{
				addErrorLine("W600 range erase is not implemented.");
				return false;
			}
			if(!doGenericSetup() || !InitialiseTarget())
				return false;
			bool ok = EraseAndWait("Erasing W600 flash...", null, 24);
			if(ok)
			{
				logger.setState("Erase done", Color.DarkGreen);
				logger.setProgress(1, 1);
				addLogLine("Erase flash ok.");
			}
			else
			{
				logger.setState("Erase error!", Color.Red);
			}
			return ok;
		}

		public override void doReadAndWrite(int startSector, int sectors, string sourceFileName, WriteMode rwMode)
		{
			if(rwMode == WriteMode.ReadAndWrite)
			{
				addErrorLine("W600 Backup then Write is not supported because the UART backup helper requires a manual reset between the read and ROM write phases. Use Backup FW first.");
				return;
			}
			if(rwMode == WriteMode.OnlyOBKConfig)
			{
				addErrorLine("Writing only OBK config is disabled for W600, use \"Automatically configure OBK on flash write\".");
				return;
			}
			if(!doGenericSetup() || !InitialiseTarget())
				return;
			try
			{
				xm.PacketSent += Xm_PacketSent;
				SetBaud(baudrate);
				OBKConfig cfg = logger.getConfigToWrite();
				if(rwMode == WriteMode.OnlyWrite)
				{
					if(string.IsNullOrEmpty(sourceFileName))
					{
						addLogLine("No filename given!");
						return;
					}
					addLogLine("Reading " + sourceFileName + "...");
					byte[] data = File.ReadAllBytes(sourceFileName);
					if(sourceFileName.EndsWith(".fls", StringComparison.OrdinalIgnoreCase))
					{
						if(xm.Send(data) != data.Length)
						{
							logger.setState("Write error!", Color.Red);
							addErrorLine("Write error!");
							return;
						}
					}
					else if(data.Length >= 0x100000)
					{
						startSector = 0x2000;
						var secBootHeader = new byte[64];
						Array.Copy(data, startSector, secBootHeader, 0, secBootHeader.Length);
						if(secBootHeader[0] != 0x9F || secBootHeader[1] != 0xFF || secBootHeader[2] != 0xFF || secBootHeader[3] != 0xA0)
						{
							addErrorLine("Unknown file type, no firmware header at 0x2000!");
							return;
						}
						if(secBootHeader[60] != 0xFF || secBootHeader[61] != 0xFF || secBootHeader[62] != 0xFF || secBootHeader[63] != 0xFF)
						{
							addErrorLine("Not W600 backup!");
							return;
						}
						var cutData = new byte[data.Length - startSector];
						Array.Copy(data, startSector, cutData, 0, cutData.Length);
						var fls = GenerateW600PseudoFLSFromData(cutData, startSector | 0x08000000);
						if(xm.Send(fls, (uint)startSector) != fls.Length)
						{
							logger.setState("Write error!", Color.Red);
							return;
						}
					}
					else
					{
						addErrorLine("Unknown file type, skipping.");
						return;
					}
					logger.setState("Writing done", Color.DarkGreen);
					addLogLine("Done flash write " + data.Length);
					logger.setProgress(1, 1);
				}

				if(cfg != null && !isCancelled)
				{
					int offset = OBKFlashLayout.getConfigLocation(chipType, out _) | 0x08000000;
					cfg.saveConfig(chipType);
					var data = MiscUtils.padArray(cfg.getData(), BK7231Flasher.SECTOR_SIZE);
					addLog("Now will also write OBK config..." + Environment.NewLine);
					addLog("Long name from CFG: " + cfg.longDeviceName + Environment.NewLine);
					addLog("Short name from CFG: " + cfg.shortDeviceName + Environment.NewLine);
					addLog("Web Root from CFG: " + cfg.webappRoot + Environment.NewLine);
					var fls = GenerateW600PseudoFLSFromData(data, offset);
					if(xm.Send(fls, (uint)(offset ^ 0x08000000)) == fls.Length)
					{
						logger.setState("OBK config write success!", Color.Green);
						logger.setProgress(1, 1);
					}
					else
					{
						logger.setState("OBK config write error!", Color.Red);
					}
				}
				else
				{
					addLog("NOTE: the OBK config writing is disabled, so not writing anything extra." + Environment.NewLine);
				}
			}
			catch(Exception ex)
			{
				addErrorLine(ex.Message);
			}
			finally
			{
				xm.PacketSent -= Xm_PacketSent;
				if(!isCancelled) SetBaud(115200, true);
			}
		}

		private static bool TryCalculateHelperBaudRegister(int baud, out uint value)
		{
			value = 0;
			if(baud != 115200 && baud != 230400 && baud != 460800 &&
				baud != 921600 && baud != 1500000 && baud != 2000000)
				return false;

			ulong divisor = (ulong)(uint)baud * 16u;
			ulong integerPart = HelperApbClock / divisor;
			if(integerPart == 0)
				return false;
			ulong remainder = HelperApbClock % divisor;
			ulong fraction = (remainder * 16u) / divisor;
			value = (uint)((integerPart - 1u) | (fraction << 16));
			return true;
		}

		private byte[] BuildHelperPayload(uint helperBaudRegister)
		{
			byte[] ramLoader = FLoaders.GetRawBinaryFromAssembly("W600_UART_Reader");
			if(ramLoader == null || ramLoader.Length != 692)
				throw new InvalidDataException("The embedded W600 UART reader has an unexpected size.");

			byte[] sector = new byte[HelperTransferPayloadSize];
			for(int i = 0; i < sector.Length; i++)
				sector[i] = 0xFF;
			for(int i = 0; i < 56; i++)
				sector[i] = 0;

			// W600 application vector table. The reset handler is a small Thumb copy stub
			// which moves the proven position-dependent reader into SRAM and branches to it.
			StoreUInt32(sector, 0x100, 0x20038000);
			StoreUInt32(sector, 0x104, HelperResetAddress);
			byte[] copyStub = new byte[]
			{
				0x72, 0xB6,             // cpsid i
				0x07, 0x48,             // ldr r0, [pc, #28] ; source
				0x07, 0x49,             // ldr r1, [pc, #28] ; destination
				0x08, 0x4A,             // ldr r2, [pc, #32] ; length
				0x03, 0x78,             // ldrb r3, [r0]
				0x0B, 0x70,             // strb r3, [r1]
				0x01, 0x30,             // adds r0, #1
				0x01, 0x31,             // adds r1, #1
				0x01, 0x3A,             // subs r2, #1
				0xF9, 0xD1,             // bne copy loop
				0x05, 0x48,             // ldr r0, [pc, #20] ; UART baud register
				0x06, 0x49,             // ldr r1, [pc, #24] ; selected baud divisor
				0x01, 0x60,             // str r1, [r0]
				0x06, 0x48,             // ldr r0, [pc, #24] ; SRAM Thumb entry
				0x00, 0x47,             // bx r0
				0x00, 0xBF,             // nop; align literal pool
			};
			Array.Copy(copyStub, 0, sector, 0x140, copyStub.Length);
			StoreUInt32(sector, 0x160, HelperPayloadAddress);
			StoreUInt32(sector, 0x164, HelperRamAddress);
			StoreUInt32(sector, 0x168, (uint)ramLoader.Length);
			StoreUInt32(sector, 0x16C, Uart0BaudRateControl);
			StoreUInt32(sector, 0x170, helperBaudRegister);
			StoreUInt32(sector, 0x174, HelperRamAddress | 1u);
			Array.Copy(ramLoader, 0, sector, 0x180, ramLoader.Length);

			int runLength = 0x80 + ramLoader.Length;
			StoreUInt32(sector, 0x00, 0xA0FFFF9F);
			StoreUInt16(sector, 0x04, 0); // old/plain image
			StoreUInt16(sector, 0x06, 0); // uncompressed
			StoreUInt32(sector, 0x08, HelperVectorAddress & 0x00FFFFFFu);
			StoreUInt32(sector, 0x0C, (uint)runLength);
			StoreUInt32(sector, 0x10,
				CRC.crc32_ver2(0xFFFFFFFF, sector, runLength, 0x100));
			byte[] version = System.Text.Encoding.ASCII.GetBytes("W600-UART-RD");
			Array.Copy(version, 0, sector, 0x24, version.Length);
			StoreUInt32(sector, 0x34, CRC.crc32_ver2(0xFFFFFFFF, sector, 0x34));
			return sector;
		}

		private bool WaitForHelper(int timeoutMs, out int chunkSize)
		{
			chunkSize = 0;
			ushort sequence = 1;
			Stopwatch total = Stopwatch.StartNew();
			long nextProgress = 0;
			while(total.ElapsedMilliseconds < timeoutMs && !isCancelled)
			{
				if(TryHelperCommand(ProtocolInfo, sequence++, 0, 0, 750,
					out byte[] payload, out string error))
				{
					if(payload.Length == 16 && ReadUInt32(payload, 0) == FlashBase &&
						ReadUInt32(payload, 4) == (uint)FlashSize)
					{
						chunkSize = ReadUInt16(payload, 8);
						if(chunkSize > 0 && chunkSize <= HelperMaxReadLength)
						{
							logger.setLogProgress(
								$"W600 UART reader ready: flash=0x{FlashBase:X8}+0x{FlashSize:X}, chunk={chunkSize}." + Environment.NewLine,
								Color.Green);
							return true;
						}
					}
				}
				if(total.ElapsedMilliseconds >= nextProgress)
				{
					logger.setLogProgress($"Waiting for W600 UART reader ({total.ElapsedMilliseconds / 1000}s)...", Color.DarkOrange);
					nextProgress = total.ElapsedMilliseconds + 5000;
				}
				Thread.Sleep(250);
			}
			return false;
		}

		private byte[] ReadHelperRange(uint address, int length, int chunkSize)
		{
			byte[] result = new byte[length];
			int completed = 0;
			int nextReport = 0x10000;
			ushort sequence = 0x100;
			while(completed < length && !isCancelled)
			{
				int requestLength = Math.Min(chunkSize, length - completed);
				byte[] payload = null;
				string lastError = "no response";
				bool ok = false;
				for(int attempt = 1; attempt <= 3 && !isCancelled; attempt++)
				{
					ok = TryHelperCommand(ProtocolRead, sequence, address + (uint)completed,
						(ushort)requestLength, 3000, out payload, out lastError);
					if(ok)
						break;
				}
				if(!ok)
				{
					logger.setLogProgress(
						$"W600 UART read failed at 0x{address + (uint)completed:X8}: {lastError}" + Environment.NewLine,
						Color.Red);
					return null;
				}
				Array.Copy(payload, 0, result, completed, payload.Length);
				completed += payload.Length;
				sequence++;
				logger.setProgress(completed, length);
				if(completed == length)
				{
					logger.setLogProgress($"Reading W600 flash: 0x{completed:X6}/0x{length:X6}. Done." + Environment.NewLine, Color.Green);
				}
				else if(completed >= nextReport)
				{
					logger.setLogProgress($"Reading W600 flash: 0x{completed:X6}/0x{length:X6}...", Color.Black);
					nextReport = completed + 0x10000;
				}
			}
			if(isCancelled)
			{
				addLogLine("W600 flash read cancelled.");
				return null;
			}
			return result;
		}

		private bool TryHelperCommand(byte opcode, ushort sequence, uint address,
			ushort length, int timeoutMs, out byte[] payload, out string error)
		{
			payload = null;
			error = "response timeout";
			byte[] request = new byte[ProtocolRequestSize];
			request[0] = (byte)'W'; request[1] = (byte)'6';
			request[2] = (byte)'R'; request[3] = (byte)'Q';
			request[4] = ProtocolVersion;
			request[5] = opcode;
			StoreUInt16(request, 6, sequence);
			StoreUInt32(request, 8, address);
			StoreUInt16(request, 12, length);
			StoreUInt16(request, 14, CRC16.Compute(CRC16Type.CCITT_FALSE, request, 0, 14));

			serial.DiscardInBuffer();
			serial.Write(request, 0, request.Length);
			Stopwatch sw = Stopwatch.StartNew();
			byte[] header = new byte[ProtocolResponseSize];
			byte[] magic = new byte[] { (byte)'W', (byte)'6', (byte)'R', (byte)'S' };
			int matched = 0;
			while(sw.ElapsedMilliseconds < timeoutMs && !isCancelled && matched < magic.Length)
			{
				if(!TryReadSerialByte(sw, timeoutMs, out byte value))
					break;
				if(value == magic[matched])
				{
					header[matched++] = value;
				}
				else
				{
					matched = value == magic[0] ? 1 : 0;
					if(matched == 1)
						header[0] = value;
				}
			}
			if(matched != magic.Length || !ReadSerialExact(header, 4, header.Length - 4, sw, timeoutMs))
				return false;
			if(CRC16.Compute(CRC16Type.CCITT_FALSE, header, 0, 18) != ReadUInt16(header, 18))
			{
				error = "response-header CRC mismatch";
				return false;
			}
			if(header[4] != ProtocolVersion || ReadUInt16(header, 6) != sequence)
			{
				error = "response version or sequence mismatch";
				return false;
			}
			if(header[5] != 0)
			{
				error = "reader status " + header[5];
				return false;
			}
			ushort responseLength = ReadUInt16(header, 12);
			if(responseLength > HelperMaxReadLength || (opcode == ProtocolRead &&
				(responseLength != length || ReadUInt32(header, 8) != address)))
			{
				error = "response range mismatch";
				return false;
			}
			payload = new byte[responseLength];
			if(!ReadSerialExact(payload, 0, payload.Length, sw, timeoutMs))
			{
				error = "response payload timeout";
				return false;
			}
			uint expectedCrc = ReadUInt32(header, 14);
			uint actualCrc = ~CRC.crc32_ver2(0xFFFFFFFF, payload);
			if(expectedCrc != actualCrc)
			{
				error = "response payload CRC mismatch";
				return false;
			}
			return true;
		}

		private bool TryReadSerialByte(Stopwatch sw, int timeoutMs, out byte value)
		{
			while(sw.ElapsedMilliseconds < timeoutMs && !isCancelled)
			{
				if(serial.BytesToRead > 0)
				{
					value = (byte)serial.ReadByte();
					return true;
				}
				Thread.Sleep(1);
			}
			value = 0;
			return false;
		}

		private bool ReadSerialExact(byte[] destination, int offset, int length,
			Stopwatch sw, int timeoutMs)
		{
			for(int i = 0; i < length; i++)
			{
				if(!TryReadSerialByte(sw, timeoutMs, out byte value))
					return false;
				destination[offset + i] = value;
			}
			return true;
		}

		private static ushort ReadUInt16(byte[] data, int offset)
		{
			return (ushort)(data[offset] | (data[offset + 1] << 8));
		}

		private static uint ReadUInt32(byte[] data, int offset)
		{
			return (uint)(data[offset] | (data[offset + 1] << 8) |
				(data[offset + 2] << 16) | (data[offset + 3] << 24));
		}

		private static void StoreUInt16(byte[] data, int offset, ushort value)
		{
			data[offset] = (byte)value;
			data[offset + 1] = (byte)(value >> 8);
		}

		private static void StoreUInt32(byte[] data, int offset, uint value)
		{
			data[offset] = (byte)value;
			data[offset + 1] = (byte)(value >> 8);
			data[offset + 2] = (byte)(value >> 16);
			data[offset + 3] = (byte)(value >> 24);
		}

		private byte[] GenerateW600PseudoFLSFromData(byte[] data, int startAddr)
		{
			var crc = CRC.crc32_ver2(0xFFFFFFFF, data);
			var fls = new List<byte>()
			{
				0x9F, 0xFF, 0xFF, 0xA0,
				0x00, 0x02, 0x00, 0x00,
				(byte)(startAddr & 0xFF), (byte)((startAddr >> 8) & 0xFF),
				(byte)((startAddr >> 16) & 0xFF), (byte)((startAddr >> 24) & 0xFF),
				(byte)(data.Length & 0xFF), (byte)((data.Length >> 8) & 0xFF),
				(byte)((data.Length >> 16) & 0xFF), (byte)((data.Length >> 24) & 0xFF),
				(byte)(crc & 0xFF), (byte)((crc >> 8) & 0xFF),
				(byte)((crc >> 16) & 0xFF), (byte)((crc >> 24) & 0xFF),
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x31, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
			};
			var headerCrc = CRC.crc32_ver2(0xFFFFFFFF, fls.ToArray());
			fls.Add((byte)(headerCrc & 0xFF));
			fls.Add((byte)((headerCrc >> 8) & 0xFF));
			fls.Add((byte)((headerCrc >> 16) & 0xFF));
			fls.Add((byte)((headerCrc >> 24) & 0xFF));
			fls.AddRange(data);
			return fls.ToArray();
		}
	}
}
