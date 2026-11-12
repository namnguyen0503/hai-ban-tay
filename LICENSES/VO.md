# Ghi công và giấy phép VO

Tất cả reference trong pipeline là âm thanh tổng hợp từ preset/model được chỉ định. Không lấy bản ghi từ YouTube, phim hoặc người thật khác để clone.

| Thành phần | License | Nguồn / ghi công |
|---|---|---|
| VieNeu-TTS v3 Turbo, model và preset Quang Sơn, Adam, Thái Sơn, Minh Triết, Thục Đoan, Mỹ Duyên | Apache-2.0 | [pnnbao97/VieNeu-TTS](https://github.com/pnnbao97/VieNeu-TTS), [model card](https://huggingface.co/pnnbao-ump/VieNeu-TTS-v3-Turbo). Ghi công tác giả pnnbao97 và các rightsholder preset theo model card. |
| Chatterbox Multilingual V3, code và weights | MIT | Resemble AI, [code/license](https://github.com/resemble-ai/chatterbox/blob/master/LICENSE), [weights](https://huggingface.co/ResembleAI/chatterbox). Giữ watermark mặc định. |
| Piper voice fr_FR-mls-medium | CC-BY-4.0 | Michael Hansen/rhasspy (model), MLS authors và LibriVox contributors (dataset). [Voice model card](https://huggingface.co/rhasspy/piper-voices/blob/main/fr/fr_FR/mls/medium/MODEL_CARD), [MLS](https://www.openslr.org/94/), [CC-BY-4.0](https://creativecommons.org/licenses/by/4.0/). Reference đã tổng hợp, trim, resample và chuẩn hóa; Chatterbox clone cho thoại Pháp, VieNeu clone cho ba câu Việt của quản bếp.  |
| Piper engine 1.8.0 | GPL-3.0-or-later | [OHF-Voice/piper1-gpl](https://github.com/OHF-Voice/piper1-gpl). Đây là engine hiện cài, khác engine Piper MIT cũ được nhắc trong nghiên cứu. Không đóng gói engine vào game. |
| faster-whisper | MIT | SYSTRAN, [license](https://github.com/SYSTRAN/faster-whisper/blob/master/LICENSE). |
| Whisper medium weights | MIT | OpenAI, [license](https://github.com/openai/whisper/blob/main/LICENSE); [bản chuyển CTranslate2](https://huggingface.co/Systran/faster-whisper-medium). Chỉ dùng kiểm tra phiên âm. |
| CTranslate2 | MIT | OpenNMT, [license](https://github.com/OpenNMT/CTranslate2/blob/master/LICENSE). |
| MOSS Audio Tokenizer Nano (VieNeu codec) | Apache-2.0 | [OpenMOSS-Team model card](https://huggingface.co/OpenMOSS-Team/MOSS-Audio-Tokenizer-Nano). |

Không dùng `fr_FR-gilles-low`; không có reference CC0 trong lần chạy này.
Nguồn metadata speaker nam: [MLS French metainfo, revision cố định](https://huggingface.co/datasets/facebook/multilingual_librispeech/blob/b710cca9cb7fb41b9bbb0d9b924725700e60bb0a/data/mls_french/metainfo.txt).
Thay đổi dữ liệu nguồn: không có. Không suy tuổi thật từ F0 hoặc metadata.
Các công cụ sinh và kiểm tra giọng đọc không được đóng gói trong bản game.
